namespace MacroDeck.Sdk.Notifications;

public enum NotificationErrorCode
{
	/// <summary>This context or host cannot show notifications.</summary>
	Unsupported,

	/// <summary>The options were rejected, for example no popup and no Inbox, or an unknown action id.</summary>
	Invalid,

	/// <summary>A count, size or rate limit was reached.</summary>
	LimitExceeded,

	/// <summary>The notification does not exist or is already closed.</summary>
	NotFound,

	/// <summary>The notification belongs to another owner.</summary>
	Forbidden
}
