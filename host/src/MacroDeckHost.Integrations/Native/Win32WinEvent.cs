using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.Native;

[SupportedOSPlatform("windows")]
internal static class Win32WinEvent
{
	public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
	public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
	public const int OBJID_WINDOW = 0;
	public const int CHILDID_SELF = 0;
	public const uint WM_QUIT = 0x0012;
	public const uint WM_USER = 0x0400;
	public const uint PM_NOREMOVE = 0x0000;

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	public delegate void WinEventProc(IntPtr hook,
		uint eventType,
		IntPtr hwnd,
		int objectId,
		int childId,
		uint threadId,
		uint eventTime);

	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MSG
	{
		public IntPtr Hwnd;
		public uint Message;
		public IntPtr WParam;
		public IntPtr LParam;
		public uint Time;
		public POINT Pt;
	}

	[DllImport("user32.dll")]
	public static extern IntPtr SetWinEventHook(uint eventMin,
		uint eventMax,
		IntPtr hmodWinEventProc,
		WinEventProc lpfnWinEventProc,
		uint idProcess,
		uint idThread,
		uint dwFlags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint msgFilterMin, uint msgFilterMax);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMsg);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool TranslateMessage(ref MSG msg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref MSG msg);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll")]
	public static extern uint GetCurrentThreadId();
}
