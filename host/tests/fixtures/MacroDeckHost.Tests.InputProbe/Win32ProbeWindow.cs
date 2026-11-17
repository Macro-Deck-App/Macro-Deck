using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Tests.InputProbe;

[SupportedOSPlatform("windows")]
internal sealed class Win32ProbeWindow
{
	private const uint CsDblClks = 0x0008;
	private const uint WsOverlappedWindow = 0x00CF0000;
	private const uint WsVisible = 0x10000000;
	private const int SwShow = 5;
	private const uint KlfActivate = 0x00000001;
	private const uint KlfSetForProcess = 0x00000100;

	private const uint WmSetFocus = 0x0007;
	private const uint WmKillFocus = 0x0008;
	private const uint WmClose = 0x0010;
	private const uint WmInputLangChange = 0x0051;
	private const uint WmKeyDown = 0x0100;
	private const uint WmKeyUp = 0x0101;
	private const uint WmChar = 0x0102;
	private const uint WmSysKeyDown = 0x0104;
	private const uint WmSysKeyUp = 0x0105;
	private const uint WmSysChar = 0x0106;
	private const uint WmSysCommand = 0x0112;
	private const uint WmMouseMove = 0x0200;
	private const uint WmLButtonDown = 0x0201;
	private const uint WmLButtonUp = 0x0202;
	private const uint WmLButtonDblClk = 0x0203;
	private const uint WmRButtonDown = 0x0204;
	private const uint WmRButtonUp = 0x0205;
	private const uint WmRButtonDblClk = 0x0206;
	private const uint WmMButtonDown = 0x0207;
	private const uint WmMButtonUp = 0x0208;
	private const uint WmMButtonDblClk = 0x0209;
	private const uint WmMouseWheel = 0x020A;
	private const uint WmXButtonDown = 0x020B;
	private const uint WmXButtonUp = 0x020C;
	private const uint WmXButtonDblClk = 0x020D;
	private const uint WmMouseHWheel = 0x020E;
	private const uint WmApp = 0x8000;
	private const uint WmAppLayout = WmApp + 1;
	private const uint WmAppQuit = WmApp + 2;
	private const nint ScKeyMenu = 0xF100;
	private const int WheelDelta = 120;

	private const int VkShift = 0x10;
	private const int VkControl = 0x11;
	private const int VkMenu = 0x12;
	private const int VkLWin = 0x5B;
	private const int VkRWin = 0x5C;
	private const int VkCapital = 0x14;

	private readonly WndProc _wndProc;
	private readonly string _title;
	private readonly int _x;
	private readonly int _y;
	private readonly int _width;
	private readonly int _height;
	private readonly Queue<string> _layouts = new();
	private readonly Lock _layoutGate = new();
	private nint _window;
	private char _pendingHighSurrogate;

	public Win32ProbeWindow(string title, int x, int y, int width, int height)
	{
		_title = title;
		_x = x;
		_y = y;
		_width = width;
		_height = height;
		_wndProc = WindowProc;
	}

	public int Run()
	{
		var instance = GetModuleHandleW(null);
		var className = "MacroDeckInputProbe" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
		var windowClass = new WndClassEx
		{
			cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
			style = CsDblClks,
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
			hInstance = instance,
			hCursor = LoadCursorW(IntPtr.Zero, 32512),
			hbrBackground = 6,
			lpszClassName = className
		};
		if (RegisterClassExW(ref windowClass) == 0)
		{
			ProbeOutput.Error($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
			return 1;
		}

		_window = CreateWindowExW(0,
			className,
			_title,
			WsOverlappedWindow | WsVisible,
			_x,
			_y,
			_width,
			_height,
			IntPtr.Zero,
			IntPtr.Zero,
			instance,
			IntPtr.Zero);
		if (_window == IntPtr.Zero)
		{
			ProbeOutput.Error($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
			return 1;
		}

		_ = ShowWindow(_window, SwShow);
		_ = UpdateWindow(_window);
		StartCommandReader();
		WriteReady();

		while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
		{
			_ = TranslateMessage(ref message);
			_ = DispatchMessageW(ref message);
		}

		return 0;
	}

	private void WriteReady()
	{
		_ = GetWindowRect(_window, out var windowRect);
		var origin = new Point();
		_ = ClientToScreen(_window, ref origin);
		_ = GetClientRect(_window, out var client);
		ProbeOutput.Write(new ProbeEvent
		{
			Type = "ready",
			Pid = Environment.ProcessId,
			Window = _window,
			X = origin.X,
			Y = origin.Y,
			Width = client.Right - client.Left,
			Height = client.Bottom - client.Top,
			Message = $"{windowRect.Left},{windowRect.Top},{windowRect.Right},{windowRect.Bottom}"
		});
	}

	private void StartCommandReader()
	{
		var thread = new Thread(() =>
		{
			while (Console.In.ReadLine() is { } line)
			{
				var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
				switch (parts.FirstOrDefault())
				{
					case "layout" when parts.Length == 2:
						lock (_layoutGate)
						{
							_layouts.Enqueue(parts[1]);
						}

						_ = PostMessageW(_window, WmAppLayout, IntPtr.Zero, IntPtr.Zero);
						break;
					case "quit":
						_ = PostMessageW(_window, WmAppQuit, IntPtr.Zero, IntPtr.Zero);
						return;
				}
			}

			_ = PostMessageW(_window, WmAppQuit, IntPtr.Zero, IntPtr.Zero);
		})
		{
			IsBackground = true
		};
		thread.Start();
	}

	private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
	{
		switch (message)
		{
			case WmKeyDown:
			case WmSysKeyDown:
				WriteKey(wParam, lParam, true);
				return 0;
			case WmKeyUp:
			case WmSysKeyUp:
				WriteKey(wParam, lParam, false);
				return 0;
			case WmChar:
			case WmSysChar:
				WriteChar((char)wParam);
				return 0;
			case WmSysCommand when (wParam & 0xFFF0) == ScKeyMenu:
				return 0;
			case WmMouseMove:
				WritePointer("move", null, null, null, lParam);
				return 0;
			case WmLButtonDown:
				return Button(window, "left", true, 1, lParam);
			case WmLButtonUp:
				return Button(window, "left", false, null, lParam);
			case WmLButtonDblClk:
				return Button(window, "left", true, 2, lParam);
			case WmRButtonDown:
				return Button(window, "right", true, 1, lParam);
			case WmRButtonUp:
				return Button(window, "right", false, null, lParam);
			case WmRButtonDblClk:
				return Button(window, "right", true, 2, lParam);
			case WmMButtonDown:
				return Button(window, "middle", true, 1, lParam);
			case WmMButtonUp:
				return Button(window, "middle", false, null, lParam);
			case WmMButtonDblClk:
				return Button(window, "middle", true, 2, lParam);
			case WmXButtonDown:
				return Button(window, XButton(wParam), true, 1, lParam);
			case WmXButtonUp:
				return Button(window, XButton(wParam), false, null, lParam);
			case WmXButtonDblClk:
				return Button(window, XButton(wParam), true, 2, lParam);
			case WmMouseWheel:
				WriteWheel("vertical", wParam, lParam);
				return 0;
			case WmMouseHWheel:
				WriteWheel("horizontal", wParam, lParam);
				return 0;
			case WmSetFocus:
				ProbeOutput.Write(new ProbeEvent { Type = "focus", Down = true });
				return 0;
			case WmKillFocus:
				ProbeOutput.Write(new ProbeEvent { Type = "focus", Down = false });
				return 0;
			case WmInputLangChange:
				ProbeOutput.Write(new ProbeEvent { Type = "layout", Layout = KeyboardLayoutName() });
				break;
			case WmAppLayout:
				ActivateQueuedLayouts();
				return 0;
			case WmAppQuit:
			case WmClose:
				PostQuitMessage(0);
				return 0;
		}

		return DefWindowProcW(window, message, wParam, lParam);
	}

	private void ActivateQueuedLayouts()
	{
		while (true)
		{
			string klid;
			lock (_layoutGate)
			{
				if (!_layouts.TryDequeue(out klid!))
				{
					return;
				}
			}

			var layout = LoadKeyboardLayoutW(klid, KlfActivate | KlfSetForProcess);
			ProbeOutput.Write(new ProbeEvent
			{
				Type = "layout",
				Layout = layout == IntPtr.Zero ? null : KeyboardLayoutName(),
				Message = layout == IntPtr.Zero ? $"LoadKeyboardLayout({klid}) failed" : klid
			});
		}
	}

	private static string KeyboardLayoutName()
	{
		var name = new char[9];
		return GetKeyboardLayoutNameW(name) ? new string(name, 0, 8) : string.Empty;
	}

	private static void WriteKey(nint wParam, nint lParam, bool down)
	{
		var bits = (long)lParam;
		ProbeOutput.Write(new ProbeEvent
		{
			Type = "key",
			Down = down,
			Code = (int)wParam,
			Scan = (int)(bits >> 16 & 0xFF),
			Extended = (bits >> 24 & 1) == 1,
			Modifiers = Modifiers()
		});
	}

	private void WriteChar(char unit)
	{
		if (char.IsHighSurrogate(unit))
		{
			_pendingHighSurrogate = unit;
			return;
		}

		var text = char.IsLowSurrogate(unit) && _pendingHighSurrogate != '\0'
			? new string([_pendingHighSurrogate, unit])
			: unit.ToString();
		_pendingHighSurrogate = '\0';
		ProbeOutput.Write(new ProbeEvent { Type = "text", Text = text });
	}

	private nint Button(nint window, string button, bool down, int? clicks, nint lParam)
	{
		if (down)
		{
			_ = SetCapture(window);
		}
		else
		{
			_ = ReleaseCapture();
		}

		WritePointer("button", button, down, clicks, lParam);
		return 0;
	}

	private void WritePointer(string type, string? button, bool? down, int? clicks, nint lParam)
	{
		var point = new Point { X = (short)(lParam & 0xFFFF), Y = (short)(lParam >> 16 & 0xFFFF) };
		_ = ClientToScreen(_window, ref point);
		ProbeOutput.Write(new ProbeEvent
		{
			Type = type,
			Button = button,
			Down = down,
			Clicks = clicks,
			X = point.X,
			Y = point.Y,
			Modifiers = Modifiers()
		});
	}

	private static void WriteWheel(string axis, nint wParam, nint lParam)
	{
		var delta = (short)(wParam >> 16 & 0xFFFF);
		ProbeOutput.Write(new ProbeEvent
		{
			Type = "scroll",
			Axis = axis,
			Delta = delta / WheelDelta,
			X = (short)(lParam & 0xFFFF),
			Y = (short)(lParam >> 16 & 0xFFFF)
		});
	}

	private static string XButton(nint wParam)
	{
		return (wParam >> 16 & 0xFFFF) == 1 ? "x1" : "x2";
	}

	private static string Modifiers()
	{
		var modifiers = new List<string>();
		if (IsDown(VkShift))
		{
			modifiers.Add("shift");
		}

		if (IsDown(VkControl))
		{
			modifiers.Add("ctrl");
		}

		if (IsDown(VkMenu))
		{
			modifiers.Add("alt");
		}

		if (IsDown(VkLWin) || IsDown(VkRWin))
		{
			modifiers.Add("meta");
		}

		if ((GetKeyState(VkCapital) & 1) != 0)
		{
			modifiers.Add("capslock");
		}

		return string.Join(',', modifiers);
	}

	private static bool IsDown(int virtualKey)
	{
		return (GetKeyState(virtualKey) & 0x8000) != 0;
	}

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WndClassEx
	{
		public uint cbSize;
		public uint style;
		public nint lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public nint hInstance;
		public nint hIcon;
		public nint hCursor;
		public nint hbrBackground;
		public string? lpszMenuName;
		public string lpszClassName;
		public nint hIconSm;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Msg
	{
		public nint hwnd;
		public uint message;
		public nint wParam;
		public nint lParam;
		public uint time;
		public Point pt;
		public uint lPrivate;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Point
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandleW(string? moduleName);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern nint CreateWindowExW(
		uint exStyle,
		string className,
		string windowName,
		uint style,
		int x,
		int y,
		int width,
		int height,
		nint parent,
		nint menu,
		nint instance,
		nint param);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint window, int command);

	[DllImport("user32.dll")]
	private static extern bool UpdateWindow(nint window);

	[DllImport("user32.dll")]
	private static extern int GetMessageW(out Msg message, nint window, uint filterMin, uint filterMax);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref Msg message);

	[DllImport("user32.dll")]
	private static extern nint DispatchMessageW(ref Msg message);

	[DllImport("user32.dll")]
	private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern void PostQuitMessage(int exitCode);

	[DllImport("user32.dll")]
	private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern nint LoadCursorW(nint instance, nint cursorName);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint window, out Rect rect);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(nint window, out Rect rect);

	[DllImport("user32.dll")]
	private static extern bool ClientToScreen(nint window, ref Point point);

	[DllImport("user32.dll")]
	private static extern nint SetCapture(nint window);

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern short GetKeyState(int virtualKey);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint LoadKeyboardLayoutW(string layoutId, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool GetKeyboardLayoutNameW([Out] char[] name);
}
