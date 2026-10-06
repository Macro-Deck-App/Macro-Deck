using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class CalendarWidgetBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _retryAfterFailure = TimeSpan.FromMinutes(1);

	private readonly CalendarWidgetTriggerScheduler _triggers;
	private readonly CalendarWidgetVariableWriter _variables;
	private readonly ICalendarEventCache _cache;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly CalendarSyncSignal _replan = new();

	public CalendarWidgetBackgroundService(
		IHostApplicationLifetime lifetime,
		CalendarWidgetTriggerScheduler triggers,
		CalendarWidgetVariableWriter variables,
		ICalendarEventCache cache,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_triggers = triggers;
		_variables = variables;
		_cache = cache;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<CalendarWidgetBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_triggers.ReplanRequested += _replan.RequestSync;
		_cache.Changed += _replan.RequestSync;
		try
		{
			await _readiness.WhenEventDispatchReady.WaitAsync(stoppingToken);

			while (!stoppingToken.IsCancellationRequested)
			{
				TimeSpan sleep;
				try
				{
					sleep = await _triggers.RunAsync(stoppingToken);

					var untilVariables = await _variables.RefreshAllAsync() - _time.GetUtcNow();
					sleep = untilVariables < sleep ? untilVariables : sleep;
					sleep = sleep < TimeSpan.Zero ? TimeSpan.Zero : sleep;
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
				{
					_logger.Error(exception, "Updating calendar widgets failed; retrying in {Delay}", _retryAfterFailure);
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
			_triggers.ReplanRequested -= _replan.RequestSync;
			_cache.Changed -= _replan.RequestSync;
		}
	}

	public override void Dispose()
	{
		_replan.Dispose();
		base.Dispose();
	}
}
