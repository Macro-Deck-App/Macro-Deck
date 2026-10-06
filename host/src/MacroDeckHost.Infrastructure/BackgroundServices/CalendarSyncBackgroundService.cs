using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class CalendarSyncBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _retryAfterFailure = TimeSpan.FromMinutes(1);

	private readonly ICalendarEventCache _cache;
	private readonly ICalendarRegistry _calendars;
	private readonly IIntegrationRegistry _integrations;
	private readonly ICalendarSyncSignal _signal;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public CalendarSyncBackgroundService(
		IHostApplicationLifetime lifetime,
		ICalendarEventCache cache,
		ICalendarRegistry calendars,
		IIntegrationRegistry integrations,
		ICalendarSyncSignal signal,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_cache = cache;
		_calendars = calendars;
		_integrations = integrations;
		_signal = signal;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<CalendarSyncBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_integrations.AvailabilityChanged += OnAvailabilityChanged;
		try
		{
			await _readiness.WhenReady.WaitAsync(stoppingToken);

			while (!stoppingToken.IsCancellationRequested)
			{
				var wait = CalendarTime.NextSyncDelay(_time.GetUtcNow(), _cache.TimeZone);
				try
				{
					await _cache.SyncAsync(stoppingToken);
					wait = CalendarTime.NextSyncDelay(_time.GetUtcNow(), _cache.TimeZone);
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
				{
					_logger.Error(exception, "Calendar sync failed; retrying in {Delay}", _retryAfterFailure);
					wait = _retryAfterFailure;
				}

				await _signal.WaitAsync(wait, _time, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			_integrations.AvailabilityChanged -= OnAvailabilityChanged;
		}
	}

	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e)
	{
		if (_calendars.IsCalendarIntegration(e.IntegrationId) ||
			_cache.Snapshot.Accounts.Any(state => state.Account.IntegrationId == e.IntegrationId))
		{
			_signal.RequestSync();
		}
	}
}
