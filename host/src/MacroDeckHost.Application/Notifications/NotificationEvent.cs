using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Application.Notifications;

public enum NotificationEventKind
{
	Shown,

	Updated,

	Closed
}

public sealed record NotificationEvent(
	NotificationEventKind Kind,
	ActiveNotification Notification,
	NotificationResult? Result = null);
