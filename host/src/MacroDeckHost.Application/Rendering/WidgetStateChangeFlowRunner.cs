using MacroDeckHost.Application.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Rendering;

public sealed class WidgetStateChangeFlowRunner
{
	private static readonly TimeSpan _maxRunDuration = TimeSpan.FromMinutes(10);

	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, int> _inFlight = new();
	private readonly HashSet<Guid> _deferred = [];

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly WidgetStateEvalChannel _queue;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly ILogger _logger;

	public WidgetStateChangeFlowRunner(
		IServiceScopeFactory scopeFactory,
		WidgetStateEvalChannel queue,
		IHostApplicationLifetime lifetime,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_queue = queue;
		_lifetime = lifetime;
		_logger = logger.ForContext<WidgetStateChangeFlowRunner>();
	}

	// A widget is not reconciled while its own flow runs, so the transitions that flow causes collapse
	// into one reconcile after it ends and settle, instead of each queueing another flow.
	public bool TryDefer(Guid widgetId)
	{
		lock (_gate)
		{
			if (!_inFlight.ContainsKey(widgetId))
			{
				return false;
			}

			_deferred.Add(widgetId);
			return true;
		}
	}

	public async Task RunInline(
		Guid widgetId,
		IFlowExecutor flowExecutor,
		FlowExecutionRequest request,
		CancellationToken cancellationToken)
	{
		Enter(widgetId);
		try
		{
			LogResult(widgetId, await flowExecutor.ExecuteAsync(request, cancellationToken));
		}
		finally
		{
			Exit(widgetId);
		}
	}

	public void RunDetached(Guid widgetId, FlowExecutionRequest request)
	{
		Enter(widgetId);
		_ = Task.Run(() => RunDetachedAsync(widgetId, request));
	}

	private async Task RunDetachedAsync(Guid widgetId, FlowExecutionRequest request)
	{
		try
		{
			using var runCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
			runCts.CancelAfter(_maxRunDuration);
			await using var scope = _scopeFactory.CreateAsyncScope();
			var flowExecutor = scope.ServiceProvider.GetRequiredService<IFlowExecutor>();
			LogResult(widgetId, await flowExecutor.ExecuteAsync(request, runCts.Token));
		}
		catch (OperationCanceledException) when (_lifetime.ApplicationStopping.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			_logger.Error(exception, "onStateChange flow for widget {WidgetId} faulted", widgetId);
		}
		finally
		{
			Exit(widgetId);
		}
	}

	private void Enter(Guid widgetId)
	{
		lock (_gate)
		{
			_inFlight[widgetId] = _inFlight.GetValueOrDefault(widgetId) + 1;
		}
	}

	private void Exit(Guid widgetId)
	{
		lock (_gate)
		{
			var remaining = _inFlight.GetValueOrDefault(widgetId) - 1;
			if (remaining > 0)
			{
				_inFlight[widgetId] = remaining;
				return;
			}

			_inFlight.Remove(widgetId);
			if (!_deferred.Remove(widgetId))
			{
				return;
			}
		}

		_queue.Enqueue(widgetId);
	}

	private void LogResult(Guid widgetId, FlowExecutionResult result)
	{
		if (result.Status == FlowExecutionStatus.Succeeded)
		{
			return;
		}

		_logger.Warning("onStateChange flow for widget {WidgetId} finished as {Status} (execution {ExecutionId}): "
			+ "{ErrorCode} {ErrorMessage}",
			widgetId,
			result.Status,
			result.ExecutionId,
			result.ErrorCode,
			result.ErrorMessage.ToString());
	}
}
