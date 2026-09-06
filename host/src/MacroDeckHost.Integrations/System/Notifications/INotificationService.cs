namespace MacroDeckHost.Integrations.System.Notifications;

public interface INotificationService
{
	bool IsSupported { get; }

	Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
