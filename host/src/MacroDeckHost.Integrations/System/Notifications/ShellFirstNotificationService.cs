namespace MacroDeckHost.Integrations.System.Notifications;

internal sealed class ShellFirstNotificationService : INotificationService
{
	private readonly IShellNotificationBridge _shell;
	private readonly INotificationService _platform;

	public ShellFirstNotificationService(IShellNotificationBridge shell, INotificationService platform)
	{
		_shell = shell;
		_platform = platform;
	}

	public bool IsSupported => _shell.IsAttached || _platform.IsSupported;

	public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		bool shown;
		try
		{
			shown = await _shell.TryDispatchAsync(title, message, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// A broken bridge must cost the notification its branding, never the notification itself.
			shown = false;
		}

		if (!shown)
		{
			await _platform.ShowAsync(title, message, cancellationToken);
		}
	}
}
