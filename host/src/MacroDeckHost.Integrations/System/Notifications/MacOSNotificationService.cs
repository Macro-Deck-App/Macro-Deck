using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Notifications;

[SupportedOSPlatform("macos")]
internal sealed class MacOsNotificationService : INotificationService
{
	public bool IsSupported => true;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		var script = $"display notification {Quote(message)} with title {Quote(title)}";
		return ProcessRunner.RunAsync("osascript", ["-e", script], cancellationToken);
	}

	private static string Quote(string value)
		=> $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
