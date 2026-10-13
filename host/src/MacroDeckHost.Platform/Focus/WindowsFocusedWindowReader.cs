using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Focus;

[SupportedOSPlatform("windows")]
public sealed class WindowsFocusedWindowReader : IFocusedWindowReader
{
	public bool IsSupported => OperatingSystem.IsWindows();

	public string? UnsupportedReason => null;

	public FocusedAppInfo? Read()
	{
		try
		{
			var window = Win32Windows.GetForegroundWindow();
			return window == IntPtr.Zero ? null : WindowsFocusedWindowResolver.Resolve(window);
		}
		catch
		{
			return null;
		}
	}
}
