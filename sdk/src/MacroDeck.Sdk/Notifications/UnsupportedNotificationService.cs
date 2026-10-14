namespace MacroDeck.Sdk.Notifications;

internal sealed class UnsupportedNotificationService : INotificationService
{
	public static readonly UnsupportedNotificationService Instance = new();

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
		return new NotificationException(NotificationErrorCode.Unsupported, "This context cannot show notifications.");
	}
}
