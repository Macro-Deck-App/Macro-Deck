namespace MacroDeck.Sdk.Notifications;

/// <summary>A notification an integration wants shown in the host's notification center.</summary>
public sealed class UserNotificationRequest
{
	public required string Title { get; init; }

	public string? Message { get; init; }

	public UserNotificationLevel Level { get; init; } = UserNotificationLevel.Info;

	/// <summary>
	/// When set, a later <see cref="IUserNotifier.Notify"/> under the same key updates this entry
	/// instead of adding a new one, and is the only key <see cref="IUserNotifier.Dismiss"/> can
	/// remove.
	/// </summary>
	public string? Key { get; init; }
}
