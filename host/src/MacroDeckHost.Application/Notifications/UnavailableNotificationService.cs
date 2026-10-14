using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Application.Notifications;

public sealed class UnavailableNotificationService : INotificationService
{
	public static readonly UnavailableNotificationService Instance = new();

	public Task<INotificationHandle> ShowAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		return Task.FromException<INotificationHandle>(Unsupported());
	}

	public Task<NotificationResult> ShowForResultAsync(
		NotificationOptions options,
		CancellationToken cancellationToken = default)
	{
		return Task.FromException<NotificationResult>(Unsupported());
	}

	private static NotificationException Unsupported()
	{
		return new NotificationException(NotificationErrorCode.Unsupported, "Notifications are not available here.");
	}
}
