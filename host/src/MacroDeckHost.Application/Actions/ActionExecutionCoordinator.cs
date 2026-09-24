using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Localization;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Actions;

public readonly record struct ActionExecutionDispatch(Guid ExecutionId, FlowExecutionResult? Result);

public interface IActionExecutionCoordinator
{
	Task<ActionExecutionDispatch> RunBoundedAsync(
		FlowExecutionRequest request,
		TimeSpan bound,
		CancellationToken cancellationToken);

	Task<ActionExecutionDispatch> RunBoundedAsync(
		Func<IServiceProvider, CancellationToken, Task<FlowExecutionResult>> run,
		TimeSpan bound,
		CancellationToken cancellationToken);
}

public sealed class ActionExecutionCoordinator : IActionExecutionCoordinator
{
	private const int MaxConcurrentRuns = 20;

	private static readonly TimeSpan _maxRunDuration = TimeSpan.FromMinutes(10);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUiTransport _transport;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly ILogger _logger;

	private int _inFlight;

	public ActionExecutionCoordinator(
		IServiceScopeFactory scopeFactory,
		IUiTransport transport,
		IHostApplicationLifetime lifetime,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_transport = transport;
		_lifetime = lifetime;
		_logger = logger.ForContext<ActionExecutionCoordinator>();
	}

	public Task<ActionExecutionDispatch> RunBoundedAsync(
		FlowExecutionRequest request,
		TimeSpan bound,
		CancellationToken cancellationToken)
		=> RunBoundedAsync(request.ExecutionId,
			(services, token) => services.GetRequiredService<IFlowExecutor>().ExecuteAsync(request, token),
			bound,
			result => PublishStatus(request, result),
			cancellationToken);

	public Task<ActionExecutionDispatch> RunBoundedAsync(
		Func<IServiceProvider, CancellationToken, Task<FlowExecutionResult>> run,
		TimeSpan bound,
		CancellationToken cancellationToken)
		=> RunBoundedAsync(Guid.NewGuid(), run, bound, _ => Task.CompletedTask, cancellationToken);

	private async Task<ActionExecutionDispatch> RunBoundedAsync(
		Guid executionId,
		Func<IServiceProvider, CancellationToken, Task<FlowExecutionResult>> execute,
		TimeSpan bound,
		Func<FlowExecutionResult, Task> detachedCompleted,
		CancellationToken cancellationToken)
	{
		if (Interlocked.Increment(ref _inFlight) > MaxConcurrentRuns)
		{
			Interlocked.Decrement(ref _inFlight);
			_logger.Warning("Rejecting flow execution {ExecutionId}: {MaxConcurrentRuns} runs already in flight",
				executionId,
				MaxConcurrentRuns);
			return new ActionExecutionDispatch(executionId, UnavailableResult(executionId));
		}

		// Linked only to the host's own shutdown token - never the caller's. ASP.NET Core cancels a
		// request's token the instant its response is sent, which would kill the run the moment we
		// answer Accepted. CancelAfter bounds a runaway While loop so a detached run cannot outlive
		// this on top of that.
		var runCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
		runCts.CancelAfter(_maxRunDuration);

		var run = RunDetached(execute, runCts.Token);

		using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var winner = await Task.WhenAny(run, Task.Delay(bound, delayCts.Token));

		delayCts.Cancel();

		if (winner == run)
		{
			runCts.Dispose();
			Interlocked.Decrement(ref _inFlight);

			FlowExecutionResult result;
			try
			{
				result = await run;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Flow execution {ExecutionId} faulted", executionId);
				result = FaultedResult(executionId);
			}

			return new ActionExecutionDispatch(executionId, result);
		}

		_ = CompleteDetached(executionId, run, runCts, detachedCompleted);
		return new ActionExecutionDispatch(executionId, null);
	}

	private async Task<FlowExecutionResult> RunDetached(
		Func<IServiceProvider, CancellationToken, Task<FlowExecutionResult>> execute,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		return await execute(scope.ServiceProvider, cancellationToken);
	}

	private async Task CompleteDetached(
		Guid executionId,
		Task<FlowExecutionResult> run,
		CancellationTokenSource runCts,
		Func<FlowExecutionResult, Task> detachedCompleted)
	{
		try
		{
			FlowExecutionResult result;
			try
			{
				result = await run;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Detached flow execution {ExecutionId} faulted", executionId);
				result = FaultedResult(executionId);
			}

			await detachedCompleted(result);
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Failed to publish the status of detached flow execution {ExecutionId}",
				executionId);
		}
		finally
		{
			runCts.Dispose();
			Interlocked.Decrement(ref _inFlight);
		}
	}

	private async Task PublishStatus(FlowExecutionRequest request, FlowExecutionResult result)
	{
		if (string.IsNullOrEmpty(request.OriginClientId))
		{
			return;
		}

		var widgetId = request.OwnerWidgetId?.ToString();
		var triggerType = request.Trigger.ByTriggerId ? null : request.Trigger.Value;
		var statusEvent = ActionExecutionDtoMapper.ToStatusEvent(result, widgetId, triggerType);
		await _transport.SendToGroup(UiClientGroups.For(request.OriginClientId), statusEvent);
	}

	private static FlowExecutionResult UnavailableResult(Guid executionId) => new()
	{
		ExecutionId = executionId,
		Status = FlowExecutionStatus.Failed,
		ErrorCode = ActionExecutionErrorCodes.Unavailable,
		ErrorMessage = AppStrings.Errors.Actions.TooManyRunning()
	};

	private static FlowExecutionResult FaultedResult(Guid executionId) => new()
	{
		ExecutionId = executionId,
		Status = FlowExecutionStatus.Failed,
		ErrorCode = ActionExecutionErrorCodes.FlowError,
		ErrorMessage = AppStrings.Errors.Actions.FlowFailedUnexpectedly()
	};
}
