using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Integrations.Clipboard.Native;

internal static class WindowsDropFiles
{
	public const int HeaderSize = 20;

	public static byte[] Build(IReadOnlyList<string> paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		var list = new StringBuilder();
		foreach (var path in paths)
		{
			list.Append(path).Append('\0');
		}

		list.Append('\0');
		var characters = Encoding.Unicode.GetBytes(list.ToString());
		var bytes = new byte[HeaderSize + characters.Length];
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), HeaderSize);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 1);
		characters.CopyTo(bytes, HeaderSize);
		return bytes;
	}
}

[SupportedOSPlatform("windows")]
internal static class WindowsClipboardNative
{
	public const uint CfText = 1;
	public const uint CfBitmap = 2;
	public const uint CfDib = 8;
	public const uint CfUnicodeText = 13;
	public const uint CfHdrop = 15;
	public const uint CfDibV5 = 17;
	public const uint GmemMoveable = 0x0002;
	public const uint WmClipboardUpdate = 0x031D;
	public const uint WmApp = 0x8000;

	public static readonly IntPtr HwndMessage = new(-3);

	public delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

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

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WndClassEx
	{
		public uint CbSize;
		public uint Style;
		public IntPtr LpfnWndProc;
		public int CbClsExtra;
		public int CbWndExtra;
		public IntPtr HInstance;
		public IntPtr HIcon;
		public IntPtr HCursor;
		public IntPtr HbrBackground;
		public IntPtr LpszMenuName;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string LpszClassName;

		public IntPtr HIconSm;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct GdiplusStartupInput
	{
		public uint GdiplusVersion;
		public IntPtr DebugEventCallback;
		public int SuppressBackgroundThread;
		public int SuppressExternalCodecs;
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool OpenClipboard(IntPtr newOwner);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseClipboard();

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool EmptyClipboard();

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr GetClipboardData(uint format);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr SetClipboardData(uint format, IntPtr memory);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool IsClipboardFormatAvailable(uint format);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int CountClipboardFormats();

	[DllImport("user32.dll")]
	public static extern uint GetClipboardSequenceNumber();

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern uint RegisterClipboardFormatW(string format);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool AddClipboardFormatListener(IntPtr hwnd);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern ushort RegisterClassExW(ref WndClassEx windowClass);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool UnregisterClassW(string className, IntPtr instance);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateWindowExW(
		uint exStyle,
		string className,
		string windowName,
		uint style,
		int x,
		int y,
		int width,
		int height,
		IntPtr parent,
		IntPtr menu,
		IntPtr instance,
		IntPtr param);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DestroyWindow(IntPtr hwnd);

	[DllImport("user32.dll")]
	public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern int GetMessageW(out Msg message, IntPtr hwnd, uint filterMin, uint filterMax);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessageW(ref Msg message);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern void PostQuitMessage(int exitCode);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr GetModuleHandleW(string? moduleName);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr GlobalFree(IntPtr memory);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr GlobalLock(IntPtr memory);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GlobalUnlock(IntPtr memory);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern nuint GlobalSize(IntPtr memory);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteObject(IntPtr gdiObject);

	[DllImport("gdiplus.dll")]
	public static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

	[DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
	public static extern int GdipCreateBitmapFromFile(string fileName, out IntPtr bitmap);

	[DllImport("gdiplus.dll")]
	public static extern int GdipCreateHBITMAPFromBitmap(IntPtr bitmap, out IntPtr hbitmap, int background);

	[DllImport("gdiplus.dll")]
	public static extern int GdipDisposeImage(IntPtr image);
}
