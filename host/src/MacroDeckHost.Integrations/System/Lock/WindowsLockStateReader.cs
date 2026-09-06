using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Lock;

[SupportedOSPlatform("windows")]
public sealed class WindowsLockStateReader : ILockStateReader
{
	private const uint DesktopSwitchDesktop = 0x0100;

	public bool IsSupported => OperatingSystem.IsWindows();

	public string? UnsupportedReason => null;

	public bool? IsLocked()
	{
		try
		{
			// The input desktop (the one receiving keyboard/mouse input) cannot be opened while the
			// secure desktop is in front - that is the signal a workstation is locked. A UAC elevation
			// prompt also raises the secure desktop, so this reads as locked for the duration of the
			// prompt too - a deliberate false positive, since it only ever causes extra blocking, never less.
			var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
			if (desktop == IntPtr.Zero)
			{
				return true;
			}

			CloseDesktop(desktop);
			return false;
		}
		catch
		{
			return null;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr OpenInputDesktop(uint flags,
		[MarshalAs(UnmanagedType.Bool)] bool inherit,
		uint desiredAccess);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseDesktop(IntPtr handle);
}
