using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.Keyboard.Native;

[SupportedOSPlatform("macos")]
public sealed class MacOsKeyboardInputProvider : IKeyboardInputProvider
{
	private const string ApplicationServices = MacOsAccessibility.ApplicationServices;

	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const ulong FlagShift = 0x00020000;
	private const ulong FlagControl = 0x00040000;
	private const ulong FlagAlternate = 0x00080000;
	private const ulong FlagCommand = 0x00100000;

	private const uint HidEventTap = 0; // kCGHIDEventTap

	private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
	private const string LibObjC = "/usr/lib/libobjc.dylib";
	private const nuint NSApplicationActivateIgnoringOtherApps = 2;

	private ulong _activeFlags;

	public string PlatformName => "macOS (Quartz CGEvent)";

	public bool IsSupported => OperatingSystem.IsMacOS();

	public bool RequiresPermission => true;

	public bool HasPermission => MacOsAccessibility.IsTrusted();

	public void RequestPermission() => MacOsAccessibility.RequestTrust();

	public void KeyDown(KeyCode key)
	{
		if (TryGetModifierFlag(key, out var flag))
		{
			_activeFlags |= flag;
		}

		PostKey(key, keyDown: true);
	}

	public void KeyUp(KeyCode key)
	{
		if (TryGetModifierFlag(key, out var flag))
		{
			_activeFlags &= ~flag;
		}

		PostKey(key, keyDown: false);
	}

	public void TypeUnicode(string text)
	{
		foreach (var rune in text.EnumerateRunes())
		{
			var units = rune.ToString().ToCharArray();
			PostUnicode(units, keyDown: true);
			PostUnicode([], keyDown: false);
		}
	}

	public bool SupportsWindowTargeting => OperatingSystem.IsMacOS();

	public bool SupportsBackgroundSend => OperatingSystem.IsMacOS();

	public string? GetForegroundProcessName()
	{
		var pid = MacOsWindows.GetForegroundOwnerPid();
		return pid is null ? null : KeyboardProcessName.ForPid(pid.Value);
	}

	public IKeyboardTargetWindow? ResolveTarget(string processName)
	{
		var processes = KeyboardProcessName.Find(processName);
		if (processes.Count == 0)
		{
			return null;
		}

		var pid = processes[0].Id;
		foreach (var process in processes)
		{
			process.Dispose();
		}

		return new MacTargetWindow(pid);
	}

	private static bool TryActivatePid(int pid)
	{
		if (!NativeLibrary.TryLoad(AppKit, out _))
		{
			return false;
		}

		var runningApplication = objc_getClass(Utf8("NSRunningApplication"));
		if (runningApplication == IntPtr.Zero)
		{
			return false;
		}

		var byPid = objc_msgSend_getByPid(runningApplication,
			sel_registerName(Utf8("runningApplicationWithProcessIdentifier:")),
			pid);
		if (byPid == IntPtr.Zero)
		{
			return false;
		}

		return objc_msgSend_activate(byPid,
			sel_registerName(Utf8("activateWithOptions:")),
			NSApplicationActivateIgnoringOtherApps);
	}

	private static byte[] Utf8(string value) => MacOsAccessibility.Utf8(value);

	private void PostKey(KeyCode key, bool keyDown)
	{
		if (!TryGetKeyCode(key, out var virtualKey))
		{
			return;
		}

		var handle = CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKey, keyDown);
		if (handle == IntPtr.Zero)
		{
			return;
		}

		try
		{
			CGEventSetFlags(handle, _activeFlags);
			CGEventPost(HidEventTap, handle);
		}
		finally
		{
			CFRelease(handle);
		}
	}

	private static void PostUnicode(char[] units, bool keyDown)
	{
		var handle = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, keyDown);
		if (handle == IntPtr.Zero)
		{
			return;
		}

		try
		{
			if (units.Length > 0)
			{
				CGEventKeyboardSetUnicodeString(handle, (nuint)units.Length, units);
			}

			CGEventPost(HidEventTap, handle);
		}
		finally
		{
			CFRelease(handle);
		}
	}

	private static bool TryGetModifierFlag(KeyCode key, out ulong flag)
	{
		flag = key switch
		{
			KeyCode.LeftShift or KeyCode.RightShift => FlagShift,
			KeyCode.LeftControl or KeyCode.RightControl => FlagControl,
			KeyCode.LeftAlt or KeyCode.RightAlt => FlagAlternate,
			KeyCode.LeftMeta or KeyCode.RightMeta => FlagCommand,
			_ => 0
		};

		return flag != 0;
	}

	private static bool TryGetKeyCode(KeyCode key, out ushort virtualKey)
	{
		virtualKey = key switch
		{
			KeyCode.A => 0, KeyCode.S => 1, KeyCode.D => 2, KeyCode.F => 3, KeyCode.H => 4,
			KeyCode.G => 5, KeyCode.Z => 6, KeyCode.X => 7, KeyCode.C => 8, KeyCode.V => 9,
			KeyCode.B => 11, KeyCode.Q => 12, KeyCode.W => 13, KeyCode.E => 14, KeyCode.R => 15,
			KeyCode.Y => 16, KeyCode.T => 17, KeyCode.O => 31, KeyCode.U => 32, KeyCode.I => 34,
			KeyCode.P => 35, KeyCode.L => 37, KeyCode.J => 38, KeyCode.K => 40, KeyCode.N => 45,
			KeyCode.M => 46,

			KeyCode.D1 => 18, KeyCode.D2 => 19, KeyCode.D3 => 20, KeyCode.D4 => 21, KeyCode.D5 => 23,
			KeyCode.D6 => 22, KeyCode.D7 => 26, KeyCode.D8 => 28, KeyCode.D9 => 25, KeyCode.D0 => 29,

			KeyCode.Equal => 24, KeyCode.Minus => 27, KeyCode.BracketRight => 30, KeyCode.BracketLeft => 33,
			KeyCode.Quote => 39, KeyCode.Semicolon => 41, KeyCode.Backslash => 42, KeyCode.Comma => 43,
			KeyCode.Slash => 44, KeyCode.Period => 47, KeyCode.Backquote => 50,

			KeyCode.Enter => 36, KeyCode.Tab => 48, KeyCode.Space => 49, KeyCode.Backspace => 51,
			KeyCode.Escape => 53, KeyCode.CapsLock => 57,

			KeyCode.LeftMeta => 55, KeyCode.RightMeta => 55,
			KeyCode.LeftShift => 56, KeyCode.RightShift => 60,
			KeyCode.LeftAlt => 58, KeyCode.RightAlt => 61,
			KeyCode.LeftControl => 59, KeyCode.RightControl => 62,

			KeyCode.F1 => 122, KeyCode.F2 => 120, KeyCode.F3 => 99, KeyCode.F4 => 118,
			KeyCode.F5 => 96, KeyCode.F6 => 97, KeyCode.F7 => 98, KeyCode.F8 => 100,
			KeyCode.F9 => 101, KeyCode.F10 => 109, KeyCode.F11 => 103, KeyCode.F12 => 111,
			KeyCode.F13 => 105, KeyCode.F14 => 107, KeyCode.F15 => 113, KeyCode.F16 => 106,
			KeyCode.F17 => 64, KeyCode.F18 => 79, KeyCode.F19 => 80, KeyCode.F20 => 90,

			KeyCode.Home => 115, KeyCode.End => 119, KeyCode.PageUp => 116, KeyCode.PageDown => 121,
			KeyCode.Delete => 117, KeyCode.Insert => 114,
			KeyCode.ArrowLeft => 123, KeyCode.ArrowRight => 124, KeyCode.ArrowDown => 125, KeyCode.ArrowUp => 126,

			KeyCode.Numpad0 => 82, KeyCode.Numpad1 => 83, KeyCode.Numpad2 => 84, KeyCode.Numpad3 => 85,
			KeyCode.Numpad4 => 86, KeyCode.Numpad5 => 87, KeyCode.Numpad6 => 88, KeyCode.Numpad7 => 89,
			KeyCode.Numpad8 => 91, KeyCode.Numpad9 => 92,
			KeyCode.NumpadDecimal => 65, KeyCode.NumpadMultiply => 67, KeyCode.NumpadAdd => 69,
			KeyCode.NumpadDivide => 75, KeyCode.NumpadEnter => 76, KeyCode.NumpadSubtract => 78,

			_ => ushort.MaxValue
		};

		return virtualKey != ushort.MaxValue;
	}

	[DllImport(ApplicationServices)]
	private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

	[DllImport(ApplicationServices)]
	private static extern void CGEventPost(uint tap, IntPtr handle);

	[DllImport(ApplicationServices)]
	private static extern void CGEventSetFlags(IntPtr handle, ulong flags);

	[DllImport(ApplicationServices, CharSet = CharSet.Unicode)]
	private static extern void CGEventKeyboardSetUnicodeString(
		IntPtr handle,
		nuint stringLength,
		[MarshalAs(UnmanagedType.LPArray)] char[] unicodeString);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr handle);

	[DllImport(ApplicationServices)]
	private static extern void CGEventPostToPid(uint pid, IntPtr handle);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_getClass(byte[] name);

	[DllImport(LibObjC)]
	private static extern IntPtr sel_registerName(byte[] name);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_getByPid(IntPtr receiver, IntPtr selector, int pid);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool objc_msgSend_activate(IntPtr receiver, IntPtr selector, nuint options);

	private sealed class MacTargetWindow : IKeyboardTargetWindow
	{
		private readonly int _pid;
		private ulong _activeFlags;

		public MacTargetWindow(int pid)
		{
			_pid = pid;
		}

		public IDisposable? Focus()
		{
			var previous = MacOsWindows.GetForegroundOwnerPid();
			if (!TryActivatePid(_pid))
			{
				return null;
			}

			Thread.Sleep(40);
			return new RestoreScope(previous);
		}

		public void KeyDown(KeyCode key)
		{
			if (TryGetModifierFlag(key, out var flag))
			{
				_activeFlags |= flag;
			}

			PostToPid(key, keyDown: true);
		}

		public void KeyUp(KeyCode key)
		{
			if (TryGetModifierFlag(key, out var flag))
			{
				_activeFlags &= ~flag;
			}

			PostToPid(key, keyDown: false);
		}

		public void TypeUnicode(string text)
		{
			foreach (var rune in text.EnumerateRunes())
			{
				var units = rune.ToString().ToCharArray();
				PostUnicodeToPid(units, keyDown: true);
				PostUnicodeToPid([], keyDown: false);
			}
		}

		public void Dispose()
		{
		}

		private void PostToPid(KeyCode key, bool keyDown)
		{
			if (!TryGetKeyCode(key, out var virtualKey))
			{
				return;
			}

			var handle = CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKey, keyDown);
			if (handle == IntPtr.Zero)
			{
				return;
			}

			try
			{
				CGEventSetFlags(handle, _activeFlags);
				CGEventPostToPid((uint)_pid, handle);
			}
			finally
			{
				CFRelease(handle);
			}
		}

		private void PostUnicodeToPid(char[] units, bool keyDown)
		{
			var handle = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, keyDown);
			if (handle == IntPtr.Zero)
			{
				return;
			}

			try
			{
				if (units.Length > 0)
				{
					CGEventKeyboardSetUnicodeString(handle, (nuint)units.Length, units);
				}

				CGEventPostToPid((uint)_pid, handle);
			}
			finally
			{
				CFRelease(handle);
			}
		}

		private sealed class RestoreScope : IDisposable
		{
			private readonly int? _previousPid;

			public RestoreScope(int? previousPid)
			{
				_previousPid = previousPid;
			}

			public void Dispose()
			{
				if (_previousPid is { } pid)
				{
					_ = TryActivatePid(pid);
				}
			}
		}
	}
}
