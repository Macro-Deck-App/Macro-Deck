using MacroDeckHost.Application.Store.Updates;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class StoreUpdatesUserNotificationHandler : INotificationHandler<StoreUpdatesChangedNotification>
{
	private readonly StoreUpdateNotifier _notifier;
	private readonly StoreAutoUpdater _autoUpdater;

	public StoreUpdatesUserNotificationHandler(StoreUpdateNotifier notifier, StoreAutoUpdater autoUpdater)
	{
		_notifier = notifier;
		_autoUpdater = autoUpdater;
	}

	// Auto-update runs first so the notification sees which updates it has taken on.
	public async ValueTask Handle(StoreUpdatesChangedNotification notification, CancellationToken cancellationToken)
	{
		await _autoUpdater.Apply(notification.Updates, cancellationToken);
		await _notifier.Notify(notification.Updates, cancellationToken);
	}
}
