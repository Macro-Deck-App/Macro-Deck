using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Notifications;

[SupportedOSPlatform("linux")]
internal sealed class LinuxNotificationService : INotificationService
{
	private readonly bool _hasNotifySend = ProcessRunner.CommandExists("notify-send");

	public bool IsSupported => _hasNotifySend;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		if (!_hasNotifySend)
		{
			return Task.CompletedTask;
		}

		return ProcessRunner.RunAsync("notify-send", [title, message], cancellationToken);
	}
}
