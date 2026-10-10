namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// The popup a notification raises on the targeted clients. A notification has at most one popup, so a
/// toast and a dialog cannot be combined. The Inbox entry is separate, see
/// <see cref="NotificationOptions.Inbox" />.
/// </summary>
public enum NotificationPopup
{
	/// <summary>No popup. Only useful together with <see cref="NotificationOptions.Inbox" />.</summary>
	None,

	/// <summary>A short overlay that goes away on its own.</summary>
	Toast,

	/// <summary>A dialog that waits for the user. It can carry <see cref="NotificationOptions.Content" />.</summary>
	Dialog
}
