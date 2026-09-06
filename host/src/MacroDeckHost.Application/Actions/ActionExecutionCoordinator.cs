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

	public async Task<ActionExecutionDispatch> RunBoundedAsync(
		FlowExecutionRequest request,
		TimeSpan bound,
		CancellationToken cancellationToken)
	{
		if (Interlocked.Increment(ref _inFlight) > MaxConcurrentRuns)
		{
			Interlocked.Decrement(ref _inFlight);
			_logger.Warning("Rejecting flow execution {ExecutionId}: {MaxConcurrentRuns} runs already in flight",
				request.ExecutionId,
				MaxConcurrentRuns);
			return new ActionExecutionDispatch(request.ExecutionId, UnavailableResult(request.ExecutionId));
		}

		// Linked only to the host's own shutdown token - never the caller's. ASP.NET Core cancels a
		// request's token the instant its response is sent, which would kill the run the moment we
		// answer Accepted. CancelAfter bounds a runaway While loop so a detached run cannot outlive
		// this on top of that.
		var runCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
		runCts.CancelAfter(_maxRunDuration);

		var run = RunDetached(request, runCts.Token);

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
				_logger.Error(ex, "Flow execution {ExecutionId} faulted", request.ExecutionId);
				result = FaultedResult(request.ExecutionId);
			}

			return new ActionExecutionDispatch(request.ExecutionId, result);
		}

		_ = PublishWhenDone(request, run, runCts);
		return new ActionExecutionDispatch(request.ExecutionId, null);
	}

	private async Task<FlowExecutionResult> RunDetached(FlowExecutionRequest request,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<IFlowExecutor>().ExecuteAsync(request, cancellationToken);
	}

	private async Task PublishWhenDone(
		FlowExecutionRequest request,
		Task<FlowExecutionResult> run,
		CancellationTokenSource runCts)
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
				_logger.Error(ex, "Detached flow execution {ExecutionId} faulted", request.ExecutionId);
				result = FaultedResult(request.ExecutionId);
			}

			if (string.IsNullOrEmpty(request.OriginClientId))
			{
				return;
			}

			var widgetId = request.OwnerWidgetId?.ToString();
			var triggerType = request.Trigger.ByTriggerId ? null : request.Trigger.Value;
			var statusEvent = ActionExecutionDtoMapper.ToStatusEvent(result, widgetId, triggerType);
			await _transport.SendToGroup(UiClientGroups.For(request.OriginClientId), statusEvent);
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Failed to publish the status of detached flow execution {ExecutionId}",
				request.ExecutionId);
		}
		finally
		{
			runCts.Dispose();
			Interlocked.Decrement(ref _inFlight);
		}
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
