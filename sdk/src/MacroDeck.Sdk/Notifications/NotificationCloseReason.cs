namespace MacroDeck.Sdk.Notifications;

public enum NotificationCloseReason
{
	/// <summary>A client pressed one of the notification's actions.</summary>
	Action,

	/// <summary>A client dismissed the popup without choosing an action.</summary>
	Dismissed,

	/// <summary>The duration set on the notification, or the host's maximum lifetime, elapsed.</summary>
	Timeout,

	/// <summary>The owner dismissed it through <see cref="INotificationHandle.DismissAsync" />.</summary>
	Programmatic,

	/// <summary>The owner went away, for example the plugin was unloaded.</summary>
	OwnerGone,

	/// <summary>The host could not be reached or restarted, so the outcome is unknown.</summary>
	HostUnavailable
}
