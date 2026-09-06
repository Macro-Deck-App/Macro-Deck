namespace MacroDeckHost.Integrations.System.Notifications;

internal sealed class NullNotificationService : INotificationService
{
	public bool IsSupported => false;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}
