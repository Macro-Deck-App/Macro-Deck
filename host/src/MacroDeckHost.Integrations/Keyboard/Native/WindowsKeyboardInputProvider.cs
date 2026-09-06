using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;
using Input = MacroDeckHost.Integrations.Native.Win32Input.Input;
using InputUnion = MacroDeckHost.Integrations.Native.Win32Input.InputUnion;
using Keybdinput = MacroDeckHost.Integrations.Native.Win32Input.Keybdinput;

namespace MacroDeckHost.Integrations.Keyboard.Native;

[SupportedOSPlatform("windows")]
public sealed class WindowsKeyboardInputProvider : IKeyboardInputProvider
{
	private const uint InputKeyboard = Win32Input.InputKeyboard;
	private const uint KeyEventFExtendedKey = 0x0001;
	private const uint KeyEventFKeyUp = 0x0002;
	private const uint KeyEventFUnicode = 0x0004;
	private const uint KeyEventFScanCode = 0x0008;
	private const uint MapVkVkToVsc = 0;

	public string PlatformName => "Windows (SendInput)";

	public bool IsSupported => OperatingSystem.IsWindows();

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public void KeyDown(KeyCode key) => SendKey(key, isUp: false);

	public void KeyUp(KeyCode key) => SendKey(key, isUp: true);

	public void TypeUnicode(string text)
	{
		var inputs = new List<Input>(text.Length * 2);
		foreach (var unit in text)
		{
			inputs.Add(UnicodeInput(unit, isUp: false));
			inputs.Add(UnicodeInput(unit, isUp: true));
		}

		Send(inputs);
	}

	public bool SupportsWindowTargeting => OperatingSystem.IsWindows();

	public bool SupportsBackgroundSend => OperatingSystem.IsWindows();

	public string? GetForegroundProcessName()
	{
		var window = Win32Windows.GetForegroundWindow();
		if (window == IntPtr.Zero)
		{
			return null;
		}

		_ = Win32Windows.GetWindowThreadProcessId(window, out var pid);
		return pid == 0 ? null : KeyboardProcessName.ForPid((int)pid);
	}

	public IKeyboardTargetWindow? ResolveTarget(string processName)
	{
		var windows = new List<IntPtr>();
		foreach (var process in KeyboardProcessName.Find(processName))
		{
			try
			{
				var handle = process.MainWindowHandle;
				if (handle != IntPtr.Zero)
				{
					windows.Add(handle);
				}
			}
			catch (InvalidOperationException)
			{
			}
			finally
			{
				process.Dispose();
			}
		}

		return windows.Count == 0 ? null : new WindowsTargetWindow(windows);
	}

	private static void SendKey(KeyCode key, bool isUp)
	{
		if (key == KeyCode.None || !TryGetVirtualKey(key, out var vk))
		{
			return;
		}

		var scan = IsMediaKey(key) ? (ushort)0 : (ushort)MapVirtualKey(vk, MapVkVkToVsc);
		var flags = KeyEventFScanCode | (isUp ? KeyEventFKeyUp : 0);
		if (IsExtendedKey(key))
		{
			flags |= KeyEventFExtendedKey;
		}

		var input = new Input
		{
			type = InputKeyboard,
			union = new InputUnion
			{
				keyboard = new Keybdinput
				{
					wVk = scan == 0 ? vk : (ushort)0,
					wScan = scan,
					dwFlags = scan == 0 ? (isUp ? KeyEventFKeyUp : 0) : flags,
					time = 0,
					dwExtraInfo = IntPtr.Zero
				}
			}
		};

		Send([input]);
	}

	private static Input UnicodeInput(char unit, bool isUp)
		=> new()
		{
			type = InputKeyboard,
			union = new InputUnion
			{
				keyboard = new Keybdinput
				{
					wVk = 0,
					wScan = unit,
					dwFlags = KeyEventFUnicode | (isUp ? KeyEventFKeyUp : 0),
					time = 0,
					dwExtraInfo = IntPtr.Zero
				}
			}
		};

	private static void Send(IReadOnlyCollection<Input> inputs) => Win32Input.Send(inputs);

	private static bool IsExtendedKey(KeyCode key) => key is
		KeyCode.Insert
		or KeyCode.Delete
		or KeyCode.Home
		or KeyCode.End
		or KeyCode.PageUp
		or KeyCode.PageDown
		or KeyCode.ArrowUp
		or KeyCode.ArrowDown
		or KeyCode.ArrowLeft
		or KeyCode.ArrowRight
		or KeyCode.RightControl
		or KeyCode.RightAlt
		or KeyCode.LeftMeta
		or KeyCode.RightMeta
		or KeyCode.NumpadDivide
		or KeyCode.NumpadEnter
		or KeyCode.PrintScreen
		or KeyCode.NumLock;

	private static bool IsMediaKey(KeyCode key) => key is
		KeyCode.MediaPlayPause
		or KeyCode.MediaStop
		or KeyCode.MediaTrackNext
		or KeyCode.MediaTrackPrevious
		or KeyCode.AudioVolumeUp
		or KeyCode.AudioVolumeDown
		or KeyCode.AudioVolumeMute;

	private static bool TryGetVirtualKey(KeyCode key, out ushort vk)
	{
		vk = key switch
		{
			>= KeyCode.A and <= KeyCode.Z => (ushort)(0x41 + (key - KeyCode.A)),
			>= KeyCode.D0 and <= KeyCode.D9 => (ushort)(0x30 + (key - KeyCode.D0)),
			>= KeyCode.F1 and <= KeyCode.F12 => (ushort)(0x70 + (key - KeyCode.F1)),
			>= KeyCode.F13 and <= KeyCode.F24 => (ushort)(0x7C + (key - KeyCode.F13)),
			>= KeyCode.Numpad0 and <= KeyCode.Numpad9 => (ushort)(0x60 + (key - KeyCode.Numpad0)),
			KeyCode.Enter => 0x0D,
			KeyCode.Escape => 0x1B,
			KeyCode.Backspace => 0x08,
			KeyCode.Tab => 0x09,
			KeyCode.Space => 0x20,
			KeyCode.CapsLock => 0x14,
			KeyCode.Insert => 0x2D,
			KeyCode.Delete => 0x2E,
			KeyCode.Home => 0x24,
			KeyCode.End => 0x23,
			KeyCode.PageUp => 0x21,
			KeyCode.PageDown => 0x22,
			KeyCode.PrintScreen => 0x2C,
			KeyCode.ScrollLock => 0x91,
			KeyCode.Pause => 0x13,
			KeyCode.ArrowUp => 0x26,
			KeyCode.ArrowDown => 0x28,
			KeyCode.ArrowLeft => 0x25,
			KeyCode.ArrowRight => 0x27,
			KeyCode.LeftShift => 0xA0,
			KeyCode.RightShift => 0xA1,
			KeyCode.LeftControl => 0xA2,
			KeyCode.RightControl => 0xA3,
			KeyCode.LeftAlt => 0xA4,
			KeyCode.RightAlt => 0xA5,
			KeyCode.LeftMeta => 0x5B,
			KeyCode.RightMeta => 0x5C,
			KeyCode.NumLock => 0x90,
			KeyCode.NumpadAdd => 0x6B,
			KeyCode.NumpadSubtract => 0x6D,
			KeyCode.NumpadMultiply => 0x6A,
			KeyCode.NumpadDivide => 0x6F,
			KeyCode.NumpadDecimal => 0x6E,
			KeyCode.NumpadEnter => 0x0D,
			KeyCode.Semicolon => 0xBA,
			KeyCode.Equal => 0xBB,
			KeyCode.Comma => 0xBC,
			KeyCode.Minus => 0xBD,
			KeyCode.Period => 0xBE,
			KeyCode.Slash => 0xBF,
			KeyCode.Backquote => 0xC0,
			KeyCode.BracketLeft => 0xDB,
			KeyCode.Backslash => 0xDC,
			KeyCode.BracketRight => 0xDD,
			KeyCode.Quote => 0xDE,
			KeyCode.MediaPlayPause => 0xB3,
			KeyCode.MediaStop => 0xB2,
			KeyCode.MediaTrackNext => 0xB0,
			KeyCode.MediaTrackPrevious => 0xB1,
			KeyCode.AudioVolumeUp => 0xAF,
			KeyCode.AudioVolumeDown => 0xAE,
			KeyCode.AudioVolumeMute => 0xAD,
			_ => 0
		};

		return vk != 0;
	}

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessage(IntPtr hWnd, uint message, nuint wParam, nint lParam);

	private sealed class WindowsTargetWindow : IKeyboardTargetWindow
	{
		// A target matching several processes must not multiply the wait: every attempt shares one budget.
		private const int ActivationBudgetMs = 500;
		private const int MinimumVerifyMs = 100;
		private const uint WmKeyDown = 0x0100;
		private const uint WmKeyUp = 0x0101;
		private const uint WmChar = 0x0102;

		private readonly IReadOnlyList<IntPtr> _windows;

		public WindowsTargetWindow(IReadOnlyList<IntPtr> windows)
		{
			_windows = windows;
		}

		public IDisposable? Focus()
		{
			var previous = Win32Windows.GetForegroundWindow();
			var deadline = Environment.TickCount64 + ActivationBudgetMs;

			foreach (var window in _windows)
			{
				var remaining = (int)Math.Clamp(deadline - Environment.TickCount64,
					MinimumVerifyMs,
					ActivationBudgetMs);
				if (Win32Foreground.TryActivate(window, remaining))
				{
					return new RestoreScope(previous);
				}
			}

			return null;
		}

		public void KeyDown(KeyCode key) => Post(key, isUp: false);

		public void KeyUp(KeyCode key) => Post(key, isUp: true);

		public void TypeUnicode(string text)
		{
			foreach (var unit in text)
			{
				foreach (var window in _windows)
				{
					_ = PostMessage(window, WmChar, unit, 0);
				}
			}
		}

		public void Dispose()
		{
		}

		private void Post(KeyCode key, bool isUp)
		{
			if (key == KeyCode.None || !TryGetVirtualKey(key, out var vk))
			{
				return;
			}

			var scan = (ushort)MapVirtualKey(vk, MapVkVkToVsc);
			var lParam = BuildLParam(scan, IsExtendedKey(key), isUp);
			var message = isUp ? WmKeyUp : WmKeyDown;
			foreach (var window in _windows)
			{
				_ = PostMessage(window, message, vk, lParam);
			}
		}

		private static nint BuildLParam(ushort scan, bool extended, bool isUp)
		{
			uint value = 1; // repeat count
			value |= (uint)scan << 16; // scan code
			if (extended)
			{
				value |= 1u << 24; // extended-key flag
			}

			if (isUp)
			{
				value |= (1u << 30) | (1u << 31); // previous key state + transition (release)
			}

			return (nint)value;
		}

		private sealed class RestoreScope : IDisposable
		{
			private readonly IntPtr _previous;

			public RestoreScope(IntPtr previous)
			{
				_previous = previous;
			}

			public void Dispose() => _ = Win32Foreground.TryActivate(_previous, verifyTimeoutMs: 0);
		}
	}
}
