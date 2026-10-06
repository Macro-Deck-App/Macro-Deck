using MacroDeckHost.Application.Events;
using Mediator;

namespace MacroDeckHost.Application.Calendar;

public sealed class CalendarIntegrationStateChangedHandler : INotificationHandler<IntegrationStateChangedNotification>
{
	private readonly ICalendarRegistry _registry;
	private readonly ICalendarEventCache _cache;
	private readonly ICalendarSyncSignal _signal;

	public CalendarIntegrationStateChangedHandler(
		ICalendarRegistry registry,
		ICalendarEventCache cache,
		ICalendarSyncSignal signal)
	{
		_registry = registry;
		_cache = cache;
		_signal = signal;
	}

	public ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
	{
		var integrationId = notification.IntegrationId;
		if (_registry.IsCalendarIntegration(integrationId) ||
			_cache.Snapshot.Accounts.Any(state => state.Account.IntegrationId == integrationId))
		{
			_signal.RequestSync();
		}

		return ValueTask.CompletedTask;
	}
}
