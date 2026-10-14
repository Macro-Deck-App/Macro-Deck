namespace MacroDeckHost.Platform.Notifications;

internal sealed class NullNotificationService : IPlatformNotificationService
{
	public bool IsSupported => false;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}
