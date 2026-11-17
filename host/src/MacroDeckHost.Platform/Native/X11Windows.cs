using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("linux")]
internal static class X11Windows
{
	private const string LibX11 = "libX11.so.6";

	// Xlib's default error handler exits the process, and a foreign window can vanish between two requests.
	private static readonly XErrorHandler _ignoreErrors = static (_, _) => 0;

	// Xlib's default IO error handler exits the process when the server goes away. Surviving takes a
	// returning global handler plus a returning per-display exit handler, which only libX11 1.7+ has.
	private static readonly ConcurrentDictionary<IntPtr, bool> _lost = new();
	private static readonly XIoErrorHandler _ignoreIoError = static _ => 0;
	private static readonly XIoErrorExitHandler _recordLoss = static (display, _) => _lost[display] = true;
	private static readonly global::System.Threading.Lock _ioHandlerLock = new();
	private static XSetIoErrorExitHandlerFn? _setExitHandler;
	private static bool _ioHandlerChecked;

	public static bool SurvivesServerLoss => InstallIoErrorHandler();

	public static IntPtr OpenDisplay(string? name = null)
	{
		_ = XSetErrorHandler(_ignoreErrors);
		var survives = InstallIoErrorHandler();
		var display = XOpenDisplay(name is null ? null : Encoding.UTF8.GetBytes(name + '\0'));
		if (display != IntPtr.Zero && survives)
		{
			_setExitHandler!(display, Marshal.GetFunctionPointerForDelegate(_recordLoss), IntPtr.Zero);
		}

		return display;
	}

	public static bool IsLost(IntPtr display)
	{
		return _lost.ContainsKey(display);
	}

	public static void CloseDisplay(IntPtr display)
	{
		_ = XCloseDisplay(display);
		Forget(display);
	}

	public static void Forget(IntPtr display)
	{
		_ = _lost.TryRemove(display, out _);
	}

	private static bool InstallIoErrorHandler()
	{
		lock (_ioHandlerLock)
		{
			if (!_ioHandlerChecked)
			{
				_ioHandlerChecked = true;
				if (NativeLibrary.TryLoad(LibX11, out var library)
					&& NativeLibrary.TryGetExport(library, "XSetIOErrorExitHandler", out var export))
				{
					_setExitHandler = Marshal.GetDelegateForFunctionPointer<XSetIoErrorExitHandlerFn>(export);
					_ = XSetIOErrorHandler(_ignoreIoError);
				}
			}

			return _setExitHandler is not null;
		}
	}

	public static nuint InternAtom(IntPtr display, string name)
	{
		return XInternAtom(display, Encoding.UTF8.GetBytes(name + '\0'), true);
	}

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

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int XIoErrorHandler(IntPtr display);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void XIoErrorExitHandler(IntPtr display, IntPtr userData);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void XSetIoErrorExitHandlerFn(IntPtr display, IntPtr handler, IntPtr userData);

	[DllImport(LibX11)]
	private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

	[DllImport(LibX11)]
	private static extern IntPtr XSetIOErrorHandler(XIoErrorHandler handler);

	[DllImport(LibX11)]
	private static extern IntPtr XOpenDisplay(byte[]? name);

	[DllImport(LibX11)]
	private static extern int XCloseDisplay(IntPtr display);

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
		out IntPtr propertyData);

	[DllImport(LibX11)]
	private static extern int XFree(IntPtr data);
}
