namespace MacroDeck.Sdk.Notifications;

/// <summary>
/// Shows a notification from the operating system of the computer Macro Deck runs on, such as a Windows toast or
/// a macOS notification, attributed to Macro Deck. Reached through <c>IIntegrationContext.DesktopNotifications</c>.
///
/// <para>
/// Use it for a message meant for the person at the computer. To show a popup or dialog on the connected Macro
/// Deck clients and wait for an answer, use <see cref="INotificationService" />; to leave an entry in Macro Deck's
/// own notification center, use <see cref="IUserNotifier" />.
/// </para>
///
/// <para>
/// Delivery is best effort: the operating system may still hide the notification, for example under a focus
/// mode, and Macro Deck reports nothing back. A plugin may use it only when its manifest declares
/// <c>host:desktop-notifications</c>; an integration built into Macro Deck always may.
/// </para>
/// </summary>
public interface IDesktopNotifier
{
	/// <summary>
	/// Whether Macro Deck can show desktop notifications for this integration: the computer offers them and,
	/// for a plugin, the manifest declares <c>host:desktop-notifications</c>. A plugin learns it when it
	/// registers; a Macro Deck that predates desktop notifications reports <c>false</c>.
	/// </summary>
	bool IsSupported { get; }

	/// <summary>
	/// Shows a notification with <paramref name="title" /> and <paramref name="message" />. Never throws because
	/// the notification could not be shown: while <see cref="IsSupported" /> is <c>false</c>, or when Macro Deck
	/// refuses or fails to show it, the call completes without showing anything.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="title" /> or <paramref name="message" /> is null.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
	Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
