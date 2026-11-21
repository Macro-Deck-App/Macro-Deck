using System.Runtime.InteropServices;

namespace MacroDeckHost.Tests.Support;

internal static class InputState
{
	private const string LibX11 = "libX11.so.6";

	private static readonly int[] _mouseButtonKeys = [0x01, 0x02, 0x04, 0x05, 0x06];

	public static IReadOnlyList<int> HeldKeys()
	{
		if (OperatingSystem.IsWindows())
		{
			return Enumerable.Range(0x08, 0xFF - 0x08)
				.Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)
				.ToArray();
		}

		if (OperatingSystem.IsLinux())
		{
			return WithDisplay(display =>
			{
				var keys = new byte[32];
				_ = XQueryKeymap(display, keys);
				return Enumerable.Range(0, 256).Where(code => (keys[code / 8] & 1 << code % 8) != 0).ToArray();
			});
		}

		return [];
	}

	public static IReadOnlyList<string> HeldButtons()
	{
		if (OperatingSystem.IsWindows())
		{
			return _mouseButtonKeys.Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)
				.Select(vk => vk.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.ToArray();
		}

		if (OperatingSystem.IsLinux())
		{
			return WithDisplay(display =>
			{
				_ = XQueryPointer(display,
					XDefaultRootWindow(display),
					out _,
					out _,
					out _,
					out _,
					out _,
					out _,
					out var mask);
				return new[] { (8, "left"), (9, "middle"), (10, "right") }
					.Where(button => (mask & 1u << button.Item1) != 0)
					.Select(button => button.Item2)
					.ToArray();
			});
		}

		return [];
	}

	public static (int X, int Y) Pointer()
	{
		if (OperatingSystem.IsWindows())
		{
			_ = GetCursorPos(out var point);
			return (point.X, point.Y);
		}

		return WithDisplay(display =>
		{
			_ = XQueryPointer(display,
				XDefaultRootWindow(display),
				out _,
				out _,
				out var x,
				out var y,
				out _,
				out _,
				out _);
			return (x, y);
		});
	}

	private static T WithDisplay<T>(Func<nint, T> read)
	{
		var display = XOpenDisplay(IntPtr.Zero);
		if (display == IntPtr.Zero)
		{
			throw new InvalidOperationException("No X display to read the input state from.");
		}

		try
		{
			return read(display);
		}
		finally
		{
			_ = XCloseDisplay(display);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Point
	{
		public int X;
		public int Y;
	}

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int virtualKey);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out Point point);

	[DllImport(LibX11)]
	private static extern nint XOpenDisplay(nint name);

	[DllImport(LibX11)]
	private static extern int XCloseDisplay(nint display);

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(nint display);

	[DllImport(LibX11)]
	private static extern int XQueryKeymap(nint display, byte[] keys);

	[DllImport(LibX11)]
	private static extern bool XQueryPointer(
		nint display,
		nuint window,
		out nuint root,
		out nuint child,
		out int rootX,
		out int rootY,
		out int windowX,
		out int windowY,
		out uint mask);
}
