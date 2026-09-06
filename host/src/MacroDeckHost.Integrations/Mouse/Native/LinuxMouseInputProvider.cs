using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Native;

[SupportedOSPlatform("linux")]
public sealed class LinuxMouseInputProvider : IMouseInputProvider, IDisposable
{
	private const string LibX11 = "libX11.so.6";
	private const string LibXtst = "libXtst.so.6";

	private const int AnyScreen = -1;

	private const uint LeftButton = 1;
	private const uint MiddleButton = 2;
	private const uint RightButton = 3;
	private const uint ScrollUpButton = 4;
	private const uint ScrollDownButton = 5;
	private const uint ScrollLeftButton = 6;
	private const uint ScrollRightButton = 7;
	private const uint BackButton = 8;
	private const uint ForwardButton = 9;

	private readonly IntPtr _display;
	private readonly nuint _root;
	private readonly int _screen;
	private readonly LocalizedText _degradedReason;

	private bool _disposed;

	public LinuxMouseInputProvider()
	{
		_display = OperatingSystem.IsLinux() ? SafeOpenDisplay() : IntPtr.Zero;
		if (_display != IntPtr.Zero)
		{
			_root = XDefaultRootWindow(_display);
			_screen = XDefaultScreen(_display);
			_degradedReason = DetectDegradation();
		}
	}

	public string PlatformName => "Linux (X11 XTest)";

	public bool IsSupported => _display != IntPtr.Zero;

	public bool IsDegraded => !_degradedReason.IsEmpty;

	public LocalizedText DegradedReason => _degradedReason;

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public bool TryGetPosition(out MousePoint position)
	{
		position = default;
		if (_display == IntPtr.Zero)
		{
			return false;
		}

		if (!XQueryPointer(_display, _root, out _, out _, out var x, out var y, out _, out _, out _))
		{
			return false;
		}

		position = new MousePoint(x, y);
		return true;
	}

	public bool TryGetDesktopBounds(out MouseRect bounds)
	{
		bounds = default;
		if (_display == IntPtr.Zero)
		{
			return false;
		}

		var width = XDisplayWidth(_display, _screen);
		var height = XDisplayHeight(_display, _screen);
		if (width <= 0 || height <= 0)
		{
			return false;
		}

		bounds = new MouseRect(0, 0, width, height);
		return true;
	}

	public void MoveTo(MousePoint position)
	{
		if (_display == IntPtr.Zero)
		{
			return;
		}

		Motion(position);
		_ = XFlush(_display);
	}

	public void ButtonDown(MouseButton button, MousePoint? position)
	{
		if (_display == IntPtr.Zero || !TryGetButtonNumber(button, out var number))
		{
			return;
		}

		if (position is { } point)
		{
			Motion(point);
		}

		FakeButton(number, press: true);
		_ = XFlush(_display);
	}

	public void ButtonUp(MouseButton button)
	{
		if (_display == IntPtr.Zero || !TryGetButtonNumber(button, out var number))
		{
			return;
		}

		FakeButton(number, press: false);
		_ = XFlush(_display);
	}

	public void Click(MouseButton button, int clickCount, MousePoint? position)
	{
		if (_display == IntPtr.Zero || !TryGetButtonNumber(button, out var number))
		{
			return;
		}

		if (position is { } point)
		{
			Motion(point);
		}

		var clicks = Math.Clamp(clickCount, 1, 3);
		for (var i = 0; i < clicks; i++)
		{
			FakeButton(number, press: true);
			FakeButton(number, press: false);
		}

		_ = XFlush(_display);
	}

	public void DragTo(MouseButton button, MousePoint position)
	{
		if (_display == IntPtr.Zero)
		{
			return;
		}

		Motion(position);
		_ = XFlush(_display);
	}

	public void Scroll(ScrollAxis axis, int notches)
	{
		if (_display == IntPtr.Zero || notches == 0)
		{
			return;
		}

		var positive = notches > 0;
		var button = axis switch
		{
			ScrollAxis.Horizontal => positive ? ScrollRightButton : ScrollLeftButton,
			_ => positive ? ScrollUpButton : ScrollDownButton
		};

		var count = Math.Abs(notches);
		for (var i = 0; i < count; i++)
		{
			FakeButton(button, press: true);
			FakeButton(button, press: false);
		}

		_ = XFlush(_display);
	}

	private void Motion(MousePoint position)
		=> _ = XTestFakeMotionEvent(_display, AnyScreen, position.X, position.Y, 0);

	private void FakeButton(uint button, bool press)
		=> _ = XTestFakeButtonEvent(_display, button, press, 0);

	private static bool TryGetButtonNumber(MouseButton button, out uint number)
	{
		number = button switch
		{
			MouseButton.Left => LeftButton,
			MouseButton.Right => RightButton,
			MouseButton.Middle => MiddleButton,
			MouseButton.Back => BackButton,
			MouseButton.Forward => ForwardButton,
			_ => 0
		};

		return number != 0;
	}

	private static IntPtr SafeOpenDisplay()
	{
		IntPtr display;
		try
		{
			display = XOpenDisplay(IntPtr.Zero);
		}
		catch (DllNotFoundException)
		{
			return IntPtr.Zero;
		}

		if (display == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}

		try
		{
			if (XTestQueryExtension(display, out _, out _, out _, out _))
			{
				return display;
			}
		}
		catch (DllNotFoundException)
		{
		}

		_ = XCloseDisplay(display);
		return IntPtr.Zero;
	}

	private static LocalizedText DetectDegradation()
	{
		var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
		var isWayland = string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) ||
			!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

		return isWayland
			? AppStrings.Integrations.Mouse.Issues.WaylandDegradedReason()
			: default;
	}

	public void Dispose()
	{
		if (_disposed || _display == IntPtr.Zero)
		{
			return;
		}

		_disposed = true;
		_ = XCloseDisplay(_display);
	}

	[DllImport(LibX11)]
	private static extern IntPtr XOpenDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XCloseDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XFlush(IntPtr display);

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XDefaultScreen(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XDisplayWidth(IntPtr display, int screen);

	[DllImport(LibX11)]
	private static extern int XDisplayHeight(IntPtr display, int screen);

	[DllImport(LibX11)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool XQueryPointer(
		IntPtr display,
		nuint window,
		out nuint rootReturn,
		out nuint childReturn,
		out int rootX,
		out int rootY,
		out int winX,
		out int winY,
		out uint maskReturn);

	[DllImport(LibXtst)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool XTestQueryExtension(
		IntPtr display,
		out int eventBase,
		out int errorBase,
		out int majorVersion,
		out int minorVersion);

	[DllImport(LibXtst)]
	private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);

	[DllImport(LibXtst)]
	private static extern int XTestFakeButtonEvent(
		IntPtr display,
		uint button,
		[MarshalAs(UnmanagedType.Bool)] bool isPress,
		nuint delay);
}
