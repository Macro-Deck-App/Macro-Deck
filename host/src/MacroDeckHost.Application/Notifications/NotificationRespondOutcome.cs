namespace MacroDeckHost.Application.Notifications;

public enum NotificationRespondStatus
{
	Accepted,

	AlreadyClosed,

	NotFound,

	Forbidden,

	Invalid
}

public sealed record NotificationRespondOutcome(NotificationRespondStatus Status, string? Message = null);
