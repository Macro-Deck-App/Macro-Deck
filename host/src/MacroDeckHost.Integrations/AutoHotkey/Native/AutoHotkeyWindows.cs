using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.AutoHotkey.Native;

[SupportedOSPlatform("windows")]
internal static class AutoHotkeyWindows
{
	private const string MainWindowClass = "AutoHotkey";
	private const uint WmClose = 0x0010;

	// Every AutoHotkey script owns a hidden main window of this class; WM_CLOSE to it runs the script's
	// OnExit callbacks with reason Close before the process ends.
	public static bool PostClose(int processId)
	{
		var posted = false;
		EnumWindows((window, _) =>
			{
				if (GetWindowThreadProcessId(window, out var owner) != 0
					&& owner == (uint)processId
					&& IsMainWindow(window))
				{
					posted |= PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
				}

				return true;
			},
			IntPtr.Zero);

		return posted;
	}

	private static bool IsMainWindow(IntPtr window)
	{
		var name = new char[64];
		var length = GetClassName(window, name, name.Length);
		return length > 0 && new string(name, 0, length) == MainWindowClass;
	}

	private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
	private static extern int GetClassName(IntPtr window, [Out] char[] className, int maxCount);

	[DllImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
