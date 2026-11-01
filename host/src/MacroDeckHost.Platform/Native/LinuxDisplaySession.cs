namespace MacroDeckHost.Platform.Native;

internal static class LinuxDisplaySession
{
	public static bool IsWayland => DetectWayland(Environment.GetEnvironmentVariable);

	internal static bool DetectWayland(Func<string, string?> environment)
	{
		return string.Equals(environment("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
			|| !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY"));
	}
}
