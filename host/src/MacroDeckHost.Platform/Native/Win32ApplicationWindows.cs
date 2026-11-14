using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("windows")]
internal static class Win32ApplicationWindows
{
	private const int GwOwner = 4;
	private const int GwlStyle = -16;
	private const int GwlExStyle = -20;
	private const long WsCaption = 0x00C00000;
	private const long WsExToolWindow = 0x00000080;
	private const uint MonitorDefaultToNearest = 2;
	private const int DwmwaCloaked = 14;
	private const int DwmCloakedShell = 0x2;

	public static void EnumerateTopLevel(Action<IntPtr> visit)
	{
		EnumWindowsProc callback = (window, _) =>
		{
			visit(window);
			return true;
		};

		_ = EnumWindows(callback, IntPtr.Zero);
		GC.KeepAlive(callback);
	}

	// The shell cloaks windows on other virtual desktops, which still count; an app-cloaked window (a
	// suspended Store app) and owned or tool windows have no taskbar entry.
	public static bool IsUserWindow(IntPtr window)
	{
		if (!IsWindowVisible(window) || GetWindow(window, GwOwner) != IntPtr.Zero)
		{
			return false;
		}

		if ((GetWindowLongPtrW(window, GwlExStyle).ToInt64() & WsExToolWindow) != 0)
		{
			return false;
		}

		return DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) != 0
			|| cloaked is 0 or DwmCloakedShell;
	}

	public static ApplicationWindowState ReadState(IntPtr window)
	{
		var iconic = IsIconic(window);
		var zoomed = IsZoomed(window);
		var hasCaption = (GetWindowLongPtrW(window, GwlStyle).ToInt64() & WsCaption) == WsCaption;

		if (!GetWindowRect(window, out var rect))
		{
			return iconic ? ApplicationWindowState.Minimized : ApplicationWindowState.Normal;
		}

		var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
		var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
		var monitorBounds = monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref monitorInfo)
			? ToBounds(monitorInfo.Monitor)
			: ToBounds(rect);

		return ApplicationWindowStateMapper.FromWindows(iconic, zoomed, hasCaption, ToBounds(rect), monitorBounds);
	}

	private static WindowBounds ToBounds(Rect rect)
	{
		return new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
	}

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(IntPtr window);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsIconic(IntPtr window);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsZoomed(IntPtr window);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr window, int command);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetWindowRect(IntPtr window, out Rect rect);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MonitorInfo
	{
		public int Size;
		public Rect Monitor;
		public Rect Work;
		public uint Flags;
	}
}
