using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Integrations.Native;

[SupportedOSPlatform("linux")]
internal static class X11Windows
{
	private const string LibX11 = "libX11.so.6";

	// Xlib's default error handler exits the process, and a foreign window can vanish between two requests.
	private static readonly XErrorHandler _ignoreErrors = static (_, _) => 0;

	public static IntPtr OpenDisplay()
	{
		_ = XSetErrorHandler(_ignoreErrors);
		return XOpenDisplay(IntPtr.Zero);
	}

	public static nuint InternAtom(IntPtr display, string name)
		=> XInternAtom(display, Encoding.UTF8.GetBytes(name + '\0'), true);

	public static List<nuint> ReadLongProperty(IntPtr display, nuint window, nuint property)
	{
		var status = XGetWindowProperty(display,
			window,
			property,
			0,
			1024,
			false,
			0,
			out _,
			out var format,
			out var itemCount,
			out _,
			out var data);

		if (status != 0 || data == IntPtr.Zero)
		{
			return [];
		}

		try
		{
			if (format != 32 || itemCount == 0)
			{
				return [];
			}

			var result = new List<nuint>((int)itemCount);
			for (nuint i = 0; i < itemCount; i++)
			{
				result.Add((nuint)Marshal.ReadIntPtr(data, (int)i * IntPtr.Size));
			}

			return result;
		}
		finally
		{
			_ = XFree(data);
		}
	}

	public static nuint GetActiveWindow(IntPtr display, nuint root)
	{
		var atom = InternAtom(display, "_NET_ACTIVE_WINDOW");
		if (atom == 0)
		{
			return 0;
		}

		var windows = ReadLongProperty(display, root, atom);
		return windows.Count > 0 ? windows[0] : 0;
	}

	public static int? GetWindowPid(IntPtr display, nuint window)
	{
		var atom = InternAtom(display, "_NET_WM_PID");
		if (atom == 0)
		{
			return null;
		}

		var values = ReadLongProperty(display, window, atom);
		return values.Count > 0 ? (int)values[0] : null;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

	[DllImport(LibX11)]
	private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

	[DllImport(LibX11)]
	private static extern IntPtr XOpenDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern nuint XInternAtom(
		IntPtr display,
		byte[] name,
		[MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

	[DllImport(LibX11)]
	private static extern int XGetWindowProperty(
		IntPtr display,
		nuint window,
		nuint property,
		nint offset,
		nint length,
		[MarshalAs(UnmanagedType.Bool)] bool delete,
		nuint requestType,
		out nuint actualType,
		out int actualFormat,
		out nuint itemCount,
		out nuint bytesAfter,
		out IntPtr property_);

	[DllImport(LibX11)]
	private static extern int XFree(IntPtr data);
}
