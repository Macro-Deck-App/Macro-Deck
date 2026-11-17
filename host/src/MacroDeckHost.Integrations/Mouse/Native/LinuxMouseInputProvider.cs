using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Platform.Native;
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

	private readonly X11Connection _connection;
	private nuint _root;
	private int _screen;
	private LocalizedText _degradedReason;

	public LinuxMouseInputProvider()
		: this(null)
	{
	}

	internal LinuxMouseInputProvider(string? displayName)
	{
		_connection = new X11Connection(Attach, () => MouseInputService.Unavailable(PlatformName), displayName);
	}

	public string PlatformName => "Linux (X11 XTest)";

	public bool IsSupported => _connection.IsAvailable;

	public bool IsDegraded => !_degradedReason.IsEmpty;

	public LocalizedText DegradedReason => _degradedReason;

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public bool TryGetPosition(out MousePoint position)
	{
		var result = _connection.RunIfConnected(QueryPointer, null);
		position = result ?? default;
		return result is not null;
	}

	public bool TryGetDesktopBounds(out MouseRect bounds)
	{
		var result = _connection.RunIfConnected(QueryDesktopBounds, null);
		bounds = result ?? default;
		return result is not null;
	}

	public void MoveTo(MousePoint position)
	{
		_connection.Run(display =>
		{
			Motion(display, position);
			_ = XFlush(display);
		});
	}

	public void ButtonDown(MouseButton button, MousePoint? position)
	{
		if (!TryGetButtonNumber(button, out var number))
		{
			return;
		}

		_connection.Run(display =>
		{
			if (position is { } point)
			{
				Motion(display, point);
			}

			FakeButton(display, number, true);
			_ = XFlush(display);
		});
	}

	public void ButtonUp(MouseButton button)
	{
		if (!TryGetButtonNumber(button, out var number))
		{
			return;
		}

		_ = _connection.TryRun(display =>
		{
			FakeButton(display, number, false);
			_ = XFlush(display);
		});
	}

	public void Click(MouseButton button, int clickCount, MousePoint? position)
	{
		if (!TryGetButtonNumber(button, out var number))
		{
			return;
		}

		_connection.Run(display =>
		{
			if (position is { } point)
			{
				Motion(display, point);
			}

			var clicks = Math.Clamp(clickCount, 1, 3);
			for (var i = 0; i < clicks; i++)
			{
				FakeButton(display, number, true);
				FakeButton(display, number, false);
			}

			_ = XFlush(display);
		});
	}

	public void DragTo(MouseButton button, MousePoint position)
	{
		_connection.Run(display =>
		{
			Motion(display, position);
			_ = XFlush(display);
		});
	}

	public void Scroll(ScrollAxis axis, int notches)
	{
		if (notches == 0)
		{
			return;
		}

		var positive = notches > 0;
		var button = axis switch
		{
			ScrollAxis.Horizontal => positive ? ScrollRightButton : ScrollLeftButton,
			_ => positive ? ScrollUpButton : ScrollDownButton
		};

		_connection.Run(display =>
		{
			var count = Math.Abs(notches);
			for (var i = 0; i < count; i++)
			{
				FakeButton(display, button, true);
				FakeButton(display, button, false);
			}

			_ = XFlush(display);
		});
	}

	private MousePoint? QueryPointer(IntPtr display)
	{
		return XQueryPointer(display, _root, out _, out _, out var x, out var y, out _, out _, out _)
			? new MousePoint(x, y)
			: null;
	}

	private MouseRect? QueryDesktopBounds(IntPtr display)
	{
		var width = XDisplayWidth(display, _screen);
		var height = XDisplayHeight(display, _screen);
		return width > 0 && height > 0 ? new MouseRect(0, 0, width, height) : null;
	}

	private static void Motion(IntPtr display, MousePoint position)
	{
		_ = XTestFakeMotionEvent(display, AnyScreen, position.X, position.Y, 0);
	}

	private static void FakeButton(IntPtr display, uint button, bool press)
	{
		_ = XTestFakeButtonEvent(display, button, press, 0);
	}

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

	private bool Attach(IntPtr display)
	{
		if (!XTestQueryExtension(display, out _, out _, out _, out _))
		{
			return false;
		}

		_root = XDefaultRootWindow(display);
		_screen = XDefaultScreen(display);
		_degradedReason = DetectDegradation();
		return true;
	}

	private static LocalizedText DetectDegradation()
	{
		return LinuxDisplaySession.IsWayland
			? AppStrings.Integrations.Mouse.Issues.WaylandDegradedReason()
			: default;
	}

	public void Dispose()
	{
		_connection.Dispose();
	}

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
