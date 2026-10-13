using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Platform.Notifications;

namespace MacroDeckHost.Infrastructure.Notifications;

public sealed class PlatformDesktopNotifier(Lazy<IPlatformNotificationService> notifications) : IDesktopNotifier
{
	public bool IsSupported => notifications.Value.IsSupported;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(message);
		cancellationToken.ThrowIfCancellationRequested();
		return notifications.Value.ShowAsync(title, message, cancellationToken);
	}
}
