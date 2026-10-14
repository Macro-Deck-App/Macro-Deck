using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("windows")]
internal static class Win32WinEvent
{
	public const uint EventSystemForeground = 0x0003;
	public const uint WineventOutofcontext = 0x0000;
	public const int ObjidWindow = 0;
	public const int ChildidSelf = 0;
	public const uint WmQuit = 0x0012;
	public const uint WmUser = 0x0400;
	public const uint PmNoremove = 0x0000;

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	public delegate void WinEventProc(
		IntPtr hook,
		uint eventType,
		IntPtr hwnd,
		int objectId,
		int childId,
		uint threadId,
		uint eventTime);

	[StructLayout(LayoutKind.Sequential)]
	public struct Point
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Msg
	{
		public IntPtr Hwnd;
		public uint Message;
		public IntPtr WParam;
		public IntPtr LParam;
		public uint Time;
		public Point Pt;
	}

	[DllImport("user32.dll")]
	public static extern IntPtr SetWinEventHook(
		uint eventMin,
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
	public static extern int GetMessage(out Msg msg, IntPtr hWnd, uint msgFilterMin, uint msgFilterMax);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PeekMessage(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMsg);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool TranslateMessage(ref Msg msg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref Msg msg);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll")]
	public static extern uint GetCurrentThreadId();
}
