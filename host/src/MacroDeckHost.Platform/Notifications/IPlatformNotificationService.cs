namespace MacroDeckHost.Platform.Notifications;

public interface IPlatformNotificationService
{
	bool IsSupported { get; }

	Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
