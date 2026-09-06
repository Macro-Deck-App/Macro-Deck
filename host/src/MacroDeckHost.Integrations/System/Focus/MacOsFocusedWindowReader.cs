using System.ComponentModel;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.System.Focus;

[SupportedOSPlatform("macos")]
public sealed class MacOsFocusedWindowReader : IFocusedWindowReader
{
	public bool IsSupported => OperatingSystem.IsMacOS();

	public string? UnsupportedReason => null;

	public FocusedAppInfo? Read()
	{
		try
		{
			if (MacOsRunningApplication.TryReadFrontmost() is not { } app)
			{
				return null;
			}

			var name = TryGetProcessName(app.ProcessId);
			return new FocusedAppInfo(app.ProcessId, app.ExecutablePath, name, app.BundleId);
		}
		catch
		{
			return null;
		}
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
