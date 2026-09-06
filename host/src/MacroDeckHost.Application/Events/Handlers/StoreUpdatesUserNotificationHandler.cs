using MacroDeckHost.Application.Notifications;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>A single aggregated notification for every extension with an update pending, rather than one
/// per package: an update queue is routine, not an event per item worth interrupting the user for.
/// </summary>
public sealed class StoreUpdatesUserNotificationHandler : INotificationHandler<StoreUpdatesChangedNotification>
{
	private const string DedupeKey = "store-updates";

	private readonly IUserNotificationStore _store;

	public StoreUpdatesUserNotificationHandler(IUserNotificationStore store)
	{
		_store = store;
	}

	public ValueTask Handle(StoreUpdatesChangedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Updates.Count == 0)
		{
			_store.Retire(DedupeKey);
			return ValueTask.CompletedTask;
		}

		var title = notification.Updates.Count == 1
			? "1 extension update available"
			: $"{notification.Updates.Count} extension updates available";

		_store.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.Update,
			Title = title,
			Message = string.Join(", ", notification.Updates.Select(update => update.Name)),
			Action = new UserNotificationAction(UserNotificationActionKind.OpenExtensionStore, null),
			DedupeKey = DedupeKey
		});

		return ValueTask.CompletedTask;
	}
}
