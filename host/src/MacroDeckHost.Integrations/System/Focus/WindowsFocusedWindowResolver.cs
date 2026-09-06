using System.ComponentModel;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.System.Focus;

[SupportedOSPlatform("windows")]
internal static class WindowsFocusedWindowResolver
{
	public static FocusedAppInfo? Resolve(IntPtr window)
	{
		if (window == IntPtr.Zero)
		{
			return null;
		}

		_ = Win32Windows.GetWindowThreadProcessId(window, out var pid);
		if (pid == 0)
		{
			return null;
		}

		var path = Win32Windows.TryQueryFullProcessImagePath(pid);
		var name = TryGetProcessName((int)pid);
		return new FocusedAppInfo((int)pid, path, name, null);
	}

	private static string? TryGetProcessName(int pid)
	{
		try
		{
			return KeyboardProcessName.ForPid(pid);
		}
		catch (Win32Exception)
		{
			return null;
		}
	}
}
