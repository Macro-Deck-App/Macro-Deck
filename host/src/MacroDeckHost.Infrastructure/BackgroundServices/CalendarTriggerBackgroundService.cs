using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class CalendarTriggerBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _retryAfterFailure = TimeSpan.FromMinutes(1);

	private readonly CalendarTriggerScheduler _scheduler;
	private readonly IEventSubscriptionIndex _index;
	private readonly ICalendarEventCache _cache;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly CalendarSyncSignal _replan = new();

	public CalendarTriggerBackgroundService(
		IHostApplicationLifetime lifetime,
		CalendarTriggerScheduler scheduler,
		IEventSubscriptionIndex index,
		ICalendarEventCache cache,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_scheduler = scheduler;
		_index = index;
		_cache = cache;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<CalendarTriggerBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_index.Changed += _replan.RequestSync;
		_cache.Changed += _replan.RequestSync;
		try
		{
			await _readiness.WhenEventDispatchReady.WaitAsync(stoppingToken);

			while (!stoppingToken.IsCancellationRequested)
			{
				TimeSpan sleep;
				try
				{
					sleep = await _scheduler.RunAsync(stoppingToken);
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
				{
					_logger.Error(exception, "Planning calendar triggers failed; retrying in {Delay}", _retryAfterFailure);
					sleep = _retryAfterFailure;
				}

				await _replan.WaitAsync(sleep, _time, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			_index.Changed -= _replan.RequestSync;
			_cache.Changed -= _replan.RequestSync;
		}
	}

	public override void Dispose()
	{
		_replan.Dispose();
		base.Dispose();
	}
}
