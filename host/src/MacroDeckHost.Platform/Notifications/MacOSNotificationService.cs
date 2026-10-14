using System.Runtime.Versioning;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Platform.Notifications;

[SupportedOSPlatform("macos")]
internal sealed class MacOsNotificationService : IPlatformNotificationService
{
	public bool IsSupported => true;

	public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		var script = $"display notification {Quote(message)} with title {Quote(title)}";
		return ProcessRunner.RunAsync("osascript", ["-e", script], cancellationToken);
	}

	private static string Quote(string value)
	{
		return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
	}
}
