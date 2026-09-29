using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Timers;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class TimerWidgetBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _tick = TimeSpan.FromMilliseconds(250);

	private readonly TimerWidgetCoordinator _timers;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public TimerWidgetBackgroundService(
		IHostApplicationLifetime lifetime,
		TimerWidgetCoordinator timers,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_timers = timers;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<TimerWidgetBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);
		await _timers.SyncAllAsync();

		using var timer = new PeriodicTimer(_tick, _time);

		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			foreach (var transition in _timers.Store.Tick())
			{
				try
				{
					await _timers.ApplyTickAsync(transition);
				}
#pragma warning disable CA1031 // One widget's failure must not stop every other timer from ticking.
				catch (Exception exception)
#pragma warning restore CA1031
				{
					_logger.Warning(exception, "Failed to apply a timer tick for widget {WidgetId}",
						transition.Snapshot.WidgetId);
				}
			}
		}
	}
}
