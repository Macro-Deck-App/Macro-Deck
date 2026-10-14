namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// Shows notifications on the connected Macro Deck clients and in the Inbox. Reached through
/// <c>IIntegrationContext.NotificationService</c>.
///
/// <para>
/// The first valid response from any targeted client wins: Macro Deck closes the popup on all of them and
/// completes the result exactly once. Macro Deck owns the timeout and the lifecycle. Notifications live in
/// memory and do not survive a host restart; a pending result then completes with
/// <see cref="NotificationCloseReason.HostUnavailable" />.
/// </para>
///
/// <para>
/// A notification is closed when its owner is unloaded. Calls throw <see cref="NotificationException" />
/// with <see cref="NotificationErrorCode.Unsupported" /> on a host that predates this service.
/// </para>
/// </summary>
public interface INotificationService
{
	/// <summary>Shows a notification and returns as soon as it is open.</summary>
	Task<INotificationHandle> ShowAsync(NotificationOptions options, CancellationToken cancellationToken = default);

	/// <summary>
	/// Shows a notification and waits until it closes. Cancelling the token dismisses the notification and
	/// throws <see cref="OperationCanceledException" />.
	/// </summary>
	Task<NotificationResult> ShowForResultAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default);
}
