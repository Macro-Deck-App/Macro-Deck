using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Tests.InputProbe;

[SupportedOSPlatform("linux")]
internal sealed class X11ProbeWindow
{
	private const string LibX11 = "libX11.so.6";
	private const string LibC = "libc";

	private const int EventSize = 192;
	private const int KeyPress = 2;
	private const int KeyRelease = 3;
	private const int ButtonPress = 4;
	private const int ButtonRelease = 5;
	private const int MotionNotify = 6;
	private const int FocusIn = 9;
	private const int FocusOut = 10;
	private const int MapNotify = 19;
	private const int ConfigureNotify = 22;
	private const int MappingNotify = 34;

	private const nint KeyPressMask = 1 << 0;
	private const nint KeyReleaseMask = 1 << 1;
	private const nint ButtonPressMask = 1 << 2;
	private const nint ButtonReleaseMask = 1 << 3;
	private const nint PointerMotionMask = 1 << 6;
	private const nint StructureNotifyMask = 1 << 17;
	private const nint FocusChangeMask = 1 << 21;
	private const nint SubstructureNotifyMask = 1 << 19;
	private const nint SubstructureRedirectMask = 1 << 20;

	private const int LcCtype = 0;
	private const nint XimPreeditNothing = 0x0008;
	private const nint XimStatusNothing = 0x0400;
	private const int ClientMessage = 33;
	private const int RevertToParent = 2;
	private const int DoubleClickMs = 400;
	private const int DoubleClickDistance = 4;

	private readonly string _title;
	private readonly int _x;
	private readonly int _y;
	private readonly int _width;
	private readonly int _height;
	private nint _display;
	private nuint _window;
	private nint _inputContext;
	private bool _ready;
	private uint _lastButton;
	private ulong _lastButtonTime;
	private int _lastButtonX;
	private int _lastButtonY;
	private int _clicks;

	public X11ProbeWindow(string title, int x, int y, int width, int height)
	{
		_title = title;
		_x = x;
		_y = y;
		_width = width;
		_height = height;
	}

	public int Run()
	{
		_ = setlocale(LcCtype, "C.UTF-8");
		_display = XOpenDisplay(null);
		if (_display == IntPtr.Zero)
		{
			ProbeOutput.Error("XOpenDisplay failed");
			return 1;
		}

		var screen = XDefaultScreen(_display);
		var root = XRootWindow(_display, screen);
		_window = XCreateSimpleWindow(_display,
			root,
			_x,
			_y,
			(uint)_width,
			(uint)_height,
			0,
			XBlackPixel(_display, screen),
			XWhitePixel(_display, screen));
		_ = XSelectInput(_display,
			_window,
			KeyPressMask
			| KeyReleaseMask
			| ButtonPressMask
			| ButtonReleaseMask
			| PointerMotionMask
			| StructureNotifyMask
			| FocusChangeMask);
		SetWindowProperties();
		OpenInputContext();
		_ = XMapRaised(_display, _window);
		_ = XFlush(_display);
		StartCommandReader();

		var buffer = Marshal.AllocHGlobal(EventSize);
		try
		{
			while (true)
			{
				_ = XNextEvent(_display, buffer);
				if (_inputContext != IntPtr.Zero && XFilterEvent(buffer, 0))
				{
					continue;
				}

				Handle(buffer);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	private void SetWindowProperties()
	{
		_ = XStoreName(_display, _window, _title);

		var className = Marshal.StringToHGlobalAnsi("macrodeck-input-probe");
		var classHint = Marshal.AllocHGlobal(2 * IntPtr.Size);
		Marshal.WriteIntPtr(classHint, 0, className);
		Marshal.WriteIntPtr(classHint, IntPtr.Size, className);
		_ = XSetClassHint(_display, _window, classHint);
		Marshal.FreeHGlobal(classHint);
		Marshal.FreeHGlobal(className);

		var hints = XAllocWMHints();
		Marshal.WriteInt64(hints, 0, 1);
		Marshal.WriteInt32(hints, 8, 1);
		_ = XSetWMHints(_display, _window, hints);
		_ = XFree(hints);

		var pid = new[] { (nint)Environment.ProcessId };
		_ = XChangeProperty(_display,
			_window,
			XInternAtom(_display, "_NET_WM_PID", false),
			XInternAtom(_display, "CARDINAL", false),
			32,
			0,
			pid,
			1);
	}

	private void OpenInputContext()
	{
		_ = XSetLocaleModifiers("@im=none");
		var inputMethod = XOpenIM(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		if (inputMethod == IntPtr.Zero)
		{
			ProbeOutput.Error("XOpenIM failed; text falls back to keysyms");
			return;
		}

		_inputContext = XCreateIC(inputMethod,
			"inputStyle",
			XimPreeditNothing | XimStatusNothing,
			"clientWindow",
			(nint)_window,
			"focusWindow",
			(nint)_window,
			IntPtr.Zero);
		if (_inputContext == IntPtr.Zero)
		{
			ProbeOutput.Error("XCreateIC failed; text falls back to keysyms");
		}
	}

	private void StartCommandReader()
	{
		var window = _window;
		var thread = new Thread(() =>
		{
			var display = XOpenDisplay(null);
			while (Console.In.ReadLine() is { } line)
			{
				switch (line.Trim())
				{
					case "focus":
						Activate(display, window);
						break;
					case "quit":
						Environment.Exit(0);
						break;
				}
			}

			Environment.Exit(0);
		})
		{
			IsBackground = true
		};
		thread.Start();
	}

	private static void Activate(nint display, nuint window)
	{
		var root = XDefaultRootWindow(display);
		var message = Marshal.AllocHGlobal(EventSize);
		try
		{
			for (var offset = 0; offset < EventSize; offset += 8)
			{
				Marshal.WriteInt64(message, offset, 0);
			}

			Marshal.WriteInt32(message, 0, ClientMessage);
			Marshal.WriteInt32(message, 16, 1);
			Marshal.WriteInt64(message, 32, (long)window);
			Marshal.WriteInt64(message, 40, (long)XInternAtom(display, "_NET_ACTIVE_WINDOW", false));
			Marshal.WriteInt32(message, 48, 32);
			Marshal.WriteInt64(message, 56, 2);
			_ = XSendEvent(display, root, false, SubstructureRedirectMask | SubstructureNotifyMask, message);
			_ = XRaiseWindow(display, window);
			_ = XSetInputFocus(display, window, RevertToParent, 0);
			_ = XFlush(display);
		}
		finally
		{
			Marshal.FreeHGlobal(message);
		}
	}

	private void Handle(nint xEvent)
	{
		var type = Marshal.ReadInt32(xEvent, 0);
		switch (type)
		{
			case KeyPress:
			case KeyRelease:
				WriteKey(xEvent, type == KeyPress);
				break;
			case ButtonPress:
			case ButtonRelease:
				WriteButton(xEvent, type == ButtonPress);
				break;
			case MotionNotify:
				ProbeOutput.Write(new ProbeEvent
				{
					Type = "move",
					X = Marshal.ReadInt32(xEvent, 72),
					Y = Marshal.ReadInt32(xEvent, 76)
				});
				break;
			case FocusIn:
			case FocusOut:
				ProbeOutput.Write(new ProbeEvent { Type = "focus", Down = type == FocusIn });
				break;
			case MapNotify:
			case ConfigureNotify:
				WriteReadyOnce();
				break;
			case MappingNotify:
				_ = XRefreshKeyboardMapping(xEvent);
				ProbeOutput.Write(new ProbeEvent { Type = "mapping" });
				break;
		}
	}

	private void WriteReadyOnce()
	{
		if (_ready)
		{
			return;
		}

		_ = XSync(_display, false);
		_ = XTranslateCoordinates(_display,
			_window,
			XDefaultRootWindow(_display),
			0,
			0,
			out var x,
			out var y,
			out _);
		_ = XGetGeometry(_display, _window, out _, out _, out _, out var width, out var height, out _, out _);
		_ready = true;
		ProbeOutput.Write(new ProbeEvent
		{
			Type = "ready",
			Pid = Environment.ProcessId,
			Window = (long)_window,
			X = x,
			Y = y,
			Width = (int)width,
			Height = (int)height
		});
	}

	private void WriteKey(nint xEvent, bool down)
	{
		var state = (uint)Marshal.ReadInt32(xEvent, 80);
		var keycode = Marshal.ReadInt32(xEvent, 84);
		var synthetic = Marshal.ReadInt32(xEvent, 16) != 0;
		var text = string.Empty;
		nuint keysym;
		if (down && _inputContext != IntPtr.Zero)
		{
			var buffer = new byte[64];
			var length = Xutf8LookupString(_inputContext, xEvent, buffer, buffer.Length, out keysym, out _);
			text = Encoding.UTF8.GetString(buffer, 0, Math.Max(0, length));
		}
		else
		{
			var buffer = new byte[64];
			var length = XLookupString(xEvent, buffer, buffer.Length, out keysym, IntPtr.Zero);
			if (down)
			{
				text = Encoding.Latin1.GetString(buffer, 0, Math.Max(0, length));
			}
		}

		ProbeOutput.Write(new ProbeEvent
		{
			Type = "key",
			Down = down,
			Code = keycode,
			Keysym = (long)keysym,
			Modifiers = Modifiers(state),
			Synthetic = synthetic ? true : null
		});

		foreach (var rune in text.EnumerateRunes())
		{
			if (!Rune.IsControl(rune))
			{
				ProbeOutput.Write(new ProbeEvent { Type = "text", Text = rune.ToString() });
			}
		}
	}

	private void WriteButton(nint xEvent, bool down)
	{
		var time = (ulong)Marshal.ReadInt64(xEvent, 56);
		var x = Marshal.ReadInt32(xEvent, 72);
		var y = Marshal.ReadInt32(xEvent, 76);
		var button = (uint)Marshal.ReadInt32(xEvent, 84);
		if (button is >= 4 and <= 7)
		{
			if (down)
			{
				ProbeOutput.Write(new ProbeEvent
				{
					Type = "scroll",
					Axis = button <= 5 ? "vertical" : "horizontal",
					Delta = button is 4 or 7 ? 1 : -1,
					X = x,
					Y = y
				});
			}

			return;
		}

		int? clicks = null;
		if (down)
		{
			var repeat = button == _lastButton
				&& time - _lastButtonTime <= DoubleClickMs
				&& Math.Abs(x - _lastButtonX) <= DoubleClickDistance
				&& Math.Abs(y - _lastButtonY) <= DoubleClickDistance;
			_clicks = repeat ? _clicks + 1 : 1;
			_lastButton = button;
			_lastButtonTime = time;
			_lastButtonX = x;
			_lastButtonY = y;
			clicks = _clicks;
		}

		ProbeOutput.Write(new ProbeEvent
		{
			Type = "button",
			Button = button switch
			{
				1 => "left",
				2 => "middle",
				3 => "right",
				8 => "x1",
				9 => "x2",
				_ => button.ToString(System.Globalization.CultureInfo.InvariantCulture)
			},
			Down = down,
			Clicks = clicks,
			X = x,
			Y = y,
			Modifiers = Modifiers((uint)Marshal.ReadInt32(xEvent, 80))
		});
	}

	private static string Modifiers(uint state)
	{
		var modifiers = new List<string>();
		if ((state & 1) != 0)
		{
			modifiers.Add("shift");
		}

		if ((state & 2) != 0)
		{
			modifiers.Add("capslock");
		}

		if ((state & 4) != 0)
		{
			modifiers.Add("ctrl");
		}

		if ((state & 8) != 0)
		{
			modifiers.Add("alt");
		}

		if ((state & 64) != 0)
		{
			modifiers.Add("meta");
		}

		if ((state & 128) != 0)
		{
			modifiers.Add("level3");
		}

		return string.Join(',', modifiers);
	}

	[DllImport(LibC)]
	private static extern nint setlocale(int category, string locale);

	[DllImport(LibX11)]
	private static extern nint XOpenDisplay(string? name);

	[DllImport(LibX11)]
	private static extern int XDefaultScreen(nint display);

	[DllImport(LibX11)]
	private static extern nuint XRootWindow(nint display, int screen);

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(nint display);

	[DllImport(LibX11)]
	private static extern nuint XBlackPixel(nint display, int screen);

	[DllImport(LibX11)]
	private static extern nuint XWhitePixel(nint display, int screen);

	[DllImport(LibX11)]
	private static extern nuint XCreateSimpleWindow(
		nint display,
		nuint parent,
		int x,
		int y,
		uint width,
		uint height,
		uint borderWidth,
		nuint border,
		nuint background);

	[DllImport(LibX11)]
	private static extern int XSelectInput(nint display, nuint window, nint mask);

	[DllImport(LibX11)]
	private static extern int XStoreName(nint display, nuint window, string name);

	[DllImport(LibX11)]
	private static extern int XSetClassHint(nint display, nuint window, nint classHint);

	[DllImport(LibX11)]
	private static extern nint XAllocWMHints();

	[DllImport(LibX11)]
	private static extern int XSetWMHints(nint display, nuint window, nint hints);

	[DllImport(LibX11)]
	private static extern int XFree(nint data);

	[DllImport(LibX11)]
	private static extern nuint XInternAtom(nint display, string name, bool onlyIfExists);

	[DllImport(LibX11)]
	private static extern int XChangeProperty(
		nint display,
		nuint window,
		nuint property,
		nuint type,
		int format,
		int mode,
		nint[] data,
		int elements);

	[DllImport(LibX11)]
	private static extern int XMapRaised(nint display, nuint window);

	[DllImport(LibX11)]
	private static extern int XRaiseWindow(nint display, nuint window);

	[DllImport(LibX11)]
	private static extern int XSetInputFocus(nint display, nuint window, int revertTo, ulong time);

	[DllImport(LibX11)]
	private static extern int XFlush(nint display);

	[DllImport(LibX11)]
	private static extern int XSync(nint display, bool discard);

	[DllImport(LibX11)]
	private static extern int XNextEvent(nint display, nint xEvent);

	[DllImport(LibX11)]
	private static extern int XSendEvent(nint display, nuint window, bool propagate, nint mask, nint xEvent);

	[DllImport(LibX11)]
	private static extern bool XFilterEvent(nint xEvent, nuint window);

	[DllImport(LibX11)]
	private static extern int XRefreshKeyboardMapping(nint xEvent);

	[DllImport(LibX11)]
	private static extern bool XTranslateCoordinates(
		nint display,
		nuint source,
		nuint destination,
		int sourceX,
		int sourceY,
		out int destinationX,
		out int destinationY,
		out nuint child);

	[DllImport(LibX11)]
	private static extern int XGetGeometry(
		nint display,
		nuint drawable,
		out nuint root,
		out int x,
		out int y,
		out uint width,
		out uint height,
		out uint borderWidth,
		out uint depth);

	[DllImport(LibX11)]
	private static extern nint XSetLocaleModifiers(string modifiers);

	[DllImport(LibX11)]
	private static extern nint XOpenIM(nint display, nint database, nint resourceName, nint resourceClass);

	[DllImport(LibX11)]
	private static extern nint XCreateIC(
		nint inputMethod,
		string name1,
		nint value1,
		string name2,
		nint value2,
		string name3,
		nint value3,
		nint terminator);

	[DllImport(LibX11)]
	private static extern int Xutf8LookupString(
		nint inputContext,
		nint xEvent,
		byte[] buffer,
		int length,
		out nuint keysym,
		out int status);

	[DllImport(LibX11)]
	private static extern int XLookupString(nint xEvent, byte[] buffer, int length, out nuint keysym, nint status);
}
