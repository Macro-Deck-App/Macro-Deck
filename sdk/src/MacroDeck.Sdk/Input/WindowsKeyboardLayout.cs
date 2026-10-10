using System.Runtime.InteropServices;

namespace MacroDeck.Sdk.Input;

internal static class WindowsKeyboardLayout
{
	private const uint MapVkVkToVsc = 0;

	public static int ScanCodeOf(int virtualKey)
	{
		if (!OperatingSystem.IsWindows())
		{
			return 0;
		}

		try
		{
			var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
			var layout = GetKeyboardLayout(thread);
			if (layout == IntPtr.Zero)
			{
				layout = GetKeyboardLayout(0);
			}

			return (int)MapVirtualKeyEx((uint)virtualKey, MapVkVkToVsc, layout);
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
		{
			return 0;
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

	[DllImport("user32.dll")]
	private static extern IntPtr GetKeyboardLayout(uint thread);

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);
}
