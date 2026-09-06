namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// Raises notifications into the host's notification center. Reached through
/// <c>IIntegrationContext.Notifications</c>; the host stamps the calling integration's id onto
/// every notification, so a provider cannot notify or dismiss under another's identity.
///
/// <para>
/// <see cref="Notify" /> is deliberately fire-and-forget and must never throw into the caller: it
/// is called from websocket callbacks and polling loops, where a call that blocks or faults would
/// take the integration's own work down with it.
/// </para>
///
/// <para>
/// <see cref="UserNotificationRequest.Key" /> makes a notification replaceable - raising twice
/// under the same key updates the existing entry instead of stacking a duplicate, so use it for
/// anything that can recur (a repeated connection failure, a repeated sync error).
/// </para>
///
/// <para>
/// Notifications are session-scoped and in-memory and do not survive a host restart, so this is
/// not a durable channel. Standing conditions that need resolving - a missing permission, invalid
/// credentials, a disconnected service - belong in <see cref="MacroDeck.Sdk.Issues.IIntegrationIssueProvider" />,
/// not here.
/// </para>
/// </summary>
public interface IUserNotifier
{
	void Notify(UserNotificationRequest notification);

	void Dismiss(string key);
}
