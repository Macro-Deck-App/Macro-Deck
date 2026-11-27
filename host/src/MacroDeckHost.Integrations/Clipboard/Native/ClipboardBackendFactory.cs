namespace MacroDeckHost.Integrations.Clipboard.Native;

internal static class ClipboardBackendFactory
{
	public static IClipboardBackend Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsClipboardBackend();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsClipboardBackend();
		}

		if (OperatingSystem.IsLinux())
		{
			return HasDesktopSession(Environment.GetEnvironmentVariable)
				? new LinuxClipboardBackend()
				: new NullClipboardBackend(
					ClipboardAvailability.Unavailable(ClipboardUnavailableReason.NoDesktopSession));
		}

		return new NullClipboardBackend(ClipboardAvailability.Unavailable(ClipboardUnavailableReason.UnsupportedOs));
	}

	internal static bool HasDesktopSession(Func<string, string?> environment)
	{
		return !string.IsNullOrEmpty(environment("DISPLAY")) || !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY"));
	}
}
