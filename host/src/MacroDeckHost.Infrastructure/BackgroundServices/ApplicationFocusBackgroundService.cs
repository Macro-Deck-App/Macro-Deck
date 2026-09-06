using MacroDeckHost.Application.Deck;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class ApplicationFocusBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _defaultRetryDelay = TimeSpan.FromSeconds(5);

	private readonly IApplicationFocusWatcher _watcher;
	private readonly IApplicationFocusCoordinator _coordinator;
	private readonly ILogger _logger;
	private readonly TimeSpan _retryDelay;

	private int? _lastPid;

	public ApplicationFocusBackgroundService(
		IHostApplicationLifetime lifetime,
		IApplicationFocusWatcher watcher,
		IApplicationFocusCoordinator coordinator,
		ILogger logger)
		: this(lifetime, watcher, coordinator, logger, _defaultRetryDelay)
	{
	}

	// Seam for tests: inject a short retry delay so a re-subscription after a faulted watch stream
	// doesn't have to wait out the real interval.
	internal ApplicationFocusBackgroundService(
		IHostApplicationLifetime lifetime,
		IApplicationFocusWatcher watcher,
		IApplicationFocusCoordinator coordinator,
		ILogger logger,
		TimeSpan retryDelay)
		: base(lifetime)
	{
		_watcher = watcher;
		_coordinator = coordinator;
		_logger = logger.ForContext<ApplicationFocusBackgroundService>();
		_retryDelay = retryDelay;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		if (!_watcher.IsSupported)
		{
			_logger.Information("Application-focus detection is unavailable: {Reason}", _watcher.UnsupportedReason);
			return;
		}

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await foreach (var app in _watcher.WatchAsync(stoppingToken))
				{
					await HandleSafely(app, stoppingToken);
				}

				// The stream ended on its own rather than faulting (e.g. the Null watcher completes
				// immediately) - there is nothing to recover from, so stop instead of spinning.
				return;
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				// A faulted BackgroundService.ExecuteTask stops the whole host by default, so a broken
				// watch stream must not propagate. Retrying after a delay - rather than ending the loop
				// like a normal completion does - keeps focus detection alive for the rest of the
				// process lifetime instead of dying silently on the first transient failure.
				_logger.Error(ex, "Application-focus watch stream failed; retrying in {RetryDelay}", _retryDelay);

				try
				{
					await Task.Delay(_retryDelay, stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
			}
		}
	}

	private async Task HandleSafely(FocusedApplication app, CancellationToken stoppingToken)
	{
		if (app.ProcessId == _lastPid)
		{
			return;
		}

		// Recorded before the coordinator runs, not after: a failed navigation must not leave the pid
		// unrecorded, or the duplicate notifications this deduplication exists to absorb would each
		// retry it.
		_lastPid = app.ProcessId;

		_logger.Debug("Focused application changed to {ProcessId} ({ProcessName})", app.ProcessId, app.ProcessName);

		try
		{
			await _coordinator.OnFocusChanged(app, stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Application-focus coordinator failed for pid {ProcessId}", app.ProcessId);
		}
	}
}
