using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Integrations.Keyboard.Native;

[SupportedOSPlatform("linux")]
public sealed class LinuxKeyboardInputProvider : IKeyboardInputProvider, IDisposable
{
	private const string LibX11 = "libX11.so.6";
	private const string LibXtst = "libXtst.so.6";

	private const int EventBufferSize = 192; // >= sizeof(XEvent) union on LP64
	private const int KeyPressType = 2;
	private const int KeyReleaseType = 3;
	private const int ClientMessageType = 33;
	private const int RevertToParent = 2;
	private const nint KeyPressMask = 1 << 0;
	private const nint KeyReleaseMask = 1 << 1;
	private const nint SubstructureNotifyMask = 1 << 19;
	private const nint SubstructureRedirectMask = 1 << 20;
	private const uint ShiftMask = 1 << 0;
	private const uint ControlMask = 1 << 2;
	private const uint Mod1Mask = 1 << 3; // Alt
	private const uint Mod4Mask = 1 << 6; // Super/Meta
	private const uint XkbUseCoreKbd = 0x0100;
	private const int XkbGroupShift = 13;
	private const nuint ReturnKeysym = 0xFF0D;
	private const nuint TabKeysym = 0xFF09;
	private const nuint ShiftLeftKeysym = 0xFFE1;
	private const nuint IsoLevel3ShiftKeysym = 0xFE03;

	private readonly IntPtr _display;
	private readonly int _spareKeycode;
	private readonly nuint _root;

	public LinuxKeyboardInputProvider()
	{
		_display = OperatingSystem.IsLinux() ? SafeOpenDisplay() : IntPtr.Zero;
		if (_display != IntPtr.Zero)
		{
			_ = XDisplayKeycodes(_display, out _, out var max);
			_spareKeycode = max; // Highest keycode is reliably unused; we borrow it for Unicode typing.
			_root = XDefaultRootWindow(_display);
		}
	}

	public string PlatformName => "Linux (X11 XTest)";

	public bool IsSupported => _display != IntPtr.Zero;

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public void KeyDown(KeyCode key)
	{
		SendKey(key, true);
	}

	public void KeyUp(KeyCode key)
	{
		SendKey(key, false);
	}

	public void TypeUnicode(string text)
	{
		if (_display == IntPtr.Zero)
		{
			return;
		}

		var layout = ReadLayout(_display);
		var level3Mask = layout.Level3Keycode == 0 ? 0 : layout.Level3Mask;
		var shiftMask = layout.ShiftKeycode == 0 ? 0 : ShiftMask;
		foreach (var keysym in ToKeysyms(text))
		{
			if (TryResolveTyping(keysym,
				XKeysymToKeycode(_display, keysym),
				layout.MinKeycode,
				layout.MaxKeycode,
				shiftMask,
				level3Mask,
				layout.Group,
				(keycode, state) => LookupKeysym(_display, keycode, state),
				out var key))
			{
				TypeKey(layout, key);
				continue;
			}

			TypeViaSpareKeycode(keysym);
		}

		_ = XFlush(_display);
	}

	internal static List<nuint> ToKeysyms(string text)
	{
		var keysyms = new List<nuint>(text.Length);
		var previousWasCarriageReturn = false;
		foreach (var rune in text.EnumerateRunes())
		{
			if (rune.Value == '\n' && previousWasCarriageReturn)
			{
				previousWasCarriageReturn = false;
				continue;
			}

			previousWasCarriageReturn = rune.Value == '\r';
			keysyms.Add(rune.Value switch
			{
				'\r' or '\n' => ReturnKeysym,
				'\t' => TabKeysym,
				<= 0xFF => (nuint)rune.Value,
				_ => (nuint)(0x01000000 + rune.Value)
			});
		}

		return keysyms;
	}

	internal static bool TryResolveTyping(
		nuint keysym,
		uint candidate,
		int minKeycode,
		int maxKeycode,
		uint shiftMask,
		uint level3Mask,
		uint group,
		Func<uint, uint, nuint> lookup,
		out TypingKey key)
	{
		var modifierSets = new List<uint> { 0 };
		if (shiftMask != 0)
		{
			modifierSets.Add(shiftMask);
		}

		if (level3Mask != 0)
		{
			modifierSets.Add(level3Mask);
			if (shiftMask != 0)
			{
				modifierSets.Add(shiftMask | level3Mask);
			}
		}

		var groupState = (group & 3) << XkbGroupShift;
		if (candidate != 0)
		{
			foreach (var modifiers in modifierSets)
			{
				if (lookup(candidate, modifiers | groupState) == keysym)
				{
					key = new TypingKey(candidate, modifiers);
					return true;
				}
			}
		}

		foreach (var modifiers in modifierSets)
		{
			for (var keycode = minKeycode; keycode <= maxKeycode; keycode++)
			{
				if (lookup((uint)keycode, modifiers | groupState) == keysym)
				{
					key = new TypingKey((uint)keycode, modifiers);
					return true;
				}
			}
		}

		key = default;
		return false;
	}

	private void TypeKey(TypingLayout layout, TypingKey key)
	{
		// A modifier the user or a Key down action already holds stays as it is, so typing never ends that hold.
		var pressShift = (key.Modifiers & ShiftMask) != 0 && (layout.ActiveModifiers & ShiftMask) == 0;
		var pressLevel3 = (key.Modifiers & layout.Level3Mask) != 0 && (layout.ActiveModifiers & layout.Level3Mask) == 0;
		if (pressShift)
		{
			FakeKey(layout.ShiftKeycode, true);
		}

		if (pressLevel3)
		{
			FakeKey(layout.Level3Keycode, true);
		}

		FakeKey(key.Keycode, true);
		FakeKey(key.Keycode, false);

		if (pressLevel3)
		{
			FakeKey(layout.Level3Keycode, false);
		}

		if (pressShift)
		{
			FakeKey(layout.ShiftKeycode, false);
		}
	}

	private static TypingLayout ReadLayout(IntPtr display)
	{
		_ = XDisplayKeycodes(display, out var min, out var max);
		var hasState = XkbGetState(display, XkbUseCoreKbd, out var state) == 0;
		return new TypingLayout(hasState ? state.Group : 0u,
			hasState ? state.Mods : 0u,
			XkbKeysymToModifiers(display, IsoLevel3ShiftKeysym),
			XKeysymToKeycode(display, ShiftLeftKeysym),
			XKeysymToKeycode(display, IsoLevel3ShiftKeysym),
			min,
			max);
	}

	private static nuint LookupKeysym(IntPtr display, uint keycode, uint state)
	{
		return XkbLookupKeySym(display, (byte)keycode, state, out _, out var keysym) ? keysym : 0;
	}

	public bool SupportsWindowTargeting => _display != IntPtr.Zero;

	public bool SupportsBackgroundSend => _display != IntPtr.Zero;

	public KeyModifier BackgroundModifiers
		=> KeyModifier.Control
			| KeyModifier.Shift
			| KeyModifier.Alt
			| KeyModifier.Meta
			| KeyModifier.RightControl
			| KeyModifier.RightShift
			| KeyModifier.RightAlt
			| KeyModifier.RightMeta;

	public string? GetForegroundProcessName()
	{
		if (_display == IntPtr.Zero)
		{
			return null;
		}

		var window = X11Windows.GetActiveWindow(_display, _root);
		if (window == 0)
		{
			return null;
		}

		var pid = X11Windows.GetWindowPid(_display, window);
		return pid is null ? null : KeyboardProcessName.ForPid(pid.Value);
	}

	public IKeyboardTargetWindow? ResolveTarget(string processName)
	{
		if (_display == IntPtr.Zero)
		{
			return null;
		}

		var pids = new HashSet<int>();
		foreach (var process in KeyboardProcessName.Find(processName))
		{
			pids.Add(process.Id);
			process.Dispose();
		}

		if (pids.Count == 0)
		{
			return null;
		}

		var clientList = X11Windows.InternAtom(_display, "_NET_CLIENT_LIST");
		if (clientList == 0)
		{
			return null;
		}

		var windows = new List<nuint>();
		foreach (var window in X11Windows.ReadLongProperty(_display, _root, clientList))
		{
			var pid = X11Windows.GetWindowPid(_display, window);
			if (pid is not null && pids.Contains(pid.Value))
			{
				windows.Add(window);
			}
		}

		return windows.Count == 0 ? null : new LinuxTargetWindow(_display, _root, windows);
	}

	private void TypeViaSpareKeycode(nuint keysym)
	{
		if (_spareKeycode <= 0)
		{
			return;
		}

		var symbols = new[] { keysym, keysym };
		_ = XChangeKeyboardMapping(_display, _spareKeycode, 2, symbols, 1);
		_ = XFlush(_display);

		FakeKey((uint)_spareKeycode, true);
		FakeKey((uint)_spareKeycode, false);
		_ = XFlush(_display);

		var cleared = new nuint[] { 0, 0 };
		_ = XChangeKeyboardMapping(_display, _spareKeycode, 2, cleared, 1);
		_ = XFlush(_display);
	}

	private void SendKey(KeyCode key, bool isPress)
	{
		if (_display == IntPtr.Zero)
		{
			return;
		}

		var keycode = ResolveKeycode(key, keysym => XKeysymToKeycode(_display, keysym));
		if (keycode == 0)
		{
			return;
		}

		FakeKey(keycode, isPress);
		_ = XFlush(_display);
	}

	private void FakeKey(uint keycode, bool press)
	{
		_ = XTestFakeKeyEvent(_display, keycode, press, 0);
	}

	private static IntPtr SafeOpenDisplay()
	{
		try
		{
			return X11Windows.OpenDisplay();
		}
		catch (DllNotFoundException)
		{
			return IntPtr.Zero;
		}
	}

	internal static uint ResolveKeycode(KeyCode key, Func<nuint, uint> keysymToKeycode)
	{
		// X servers on the evdev/libinput driver (Xorg and XWayland) number keys as evdev code plus 8.
		const uint evdevOffset = 8;
		if (NativeKeys.IsPositional(key))
		{
			return NativeKeys.TryGetWindowsScanCode(key, out var position) ? position + evdevOffset : 0;
		}

		if (!TryGetKeysym(key, out var keysym))
		{
			return 0;
		}

		var keycode = keysymToKeycode(keysym);
		if (keycode != 0)
		{
			return keycode;
		}

		if (key is >= KeyCode.A and <= KeyCode.Z && NativeKeys.TryGetWindowsScanCode(key, out var letter))
		{
			return letter + evdevOffset;
		}

		// A layout can put another keysym on a modifier key (AltGr puts ISO_Level3_Shift on Right Alt),
		// so a modifier no key is labelled with presses its physical key.
		return TryGetModifierEvdevCode(key, out var modifier) ? modifier + evdevOffset : 0;
	}

	internal static uint ResolveStateMask(
		KeyCode key,
		uint keycode,
		Func<uint, nuint> lookup,
		Func<nuint, uint> keysymToModifiers)
	{
		if (!TryGetStateMask(key, out var fallback))
		{
			return 0;
		}

		var keysym = lookup(keycode);
		return keysym == 0 ? fallback : keysymToModifiers(keysym);
	}

	private static bool TryGetModifierEvdevCode(KeyCode key, out uint evdevCode)
	{
		evdevCode = key switch
		{
			KeyCode.LeftControl => 29,
			KeyCode.LeftShift => 42,
			KeyCode.RightShift => 54,
			KeyCode.LeftAlt => 56,
			KeyCode.RightControl => 97,
			KeyCode.RightAlt => 100,
			KeyCode.LeftMeta => 125,
			KeyCode.RightMeta => 126,
			_ => 0u
		};

		return evdevCode != 0;
	}

	private static bool TryGetKeysym(KeyCode key, out nuint keysym)
	{
		keysym = key switch
		{
			>= KeyCode.A and <= KeyCode.Z => (nuint)(0x61 + (key - KeyCode.A)),
			>= KeyCode.D0 and <= KeyCode.D9 => (nuint)(0x30 + (key - KeyCode.D0)),
			>= KeyCode.F1 and <= KeyCode.F12 => (nuint)(0xFFBE + (key - KeyCode.F1)),
			>= KeyCode.F13 and <= KeyCode.F24 => (nuint)(0xFFCA + (key - KeyCode.F13)),
			>= KeyCode.Numpad0 and <= KeyCode.Numpad9 => (nuint)(0xFFB0 + (key - KeyCode.Numpad0)),
			KeyCode.Enter => 0xFF0D,
			KeyCode.Escape => 0xFF1B,
			KeyCode.Backspace => 0xFF08,
			KeyCode.Tab => 0xFF09,
			KeyCode.Space => 0x0020,
			KeyCode.CapsLock => 0xFFE5,
			KeyCode.Insert => 0xFF63,
			KeyCode.Delete => 0xFFFF,
			KeyCode.Home => 0xFF50,
			KeyCode.End => 0xFF57,
			KeyCode.PageUp => 0xFF55,
			KeyCode.PageDown => 0xFF56,
			KeyCode.PrintScreen => 0xFF61,
			KeyCode.ScrollLock => 0xFF14,
			KeyCode.Pause => 0xFF13,
			KeyCode.ArrowUp => 0xFF52,
			KeyCode.ArrowDown => 0xFF54,
			KeyCode.ArrowLeft => 0xFF51,
			KeyCode.ArrowRight => 0xFF53,
			KeyCode.LeftShift => 0xFFE1,
			KeyCode.RightShift => 0xFFE2,
			KeyCode.LeftControl => 0xFFE3,
			KeyCode.RightControl => 0xFFE4,
			KeyCode.LeftAlt => 0xFFE9,
			KeyCode.RightAlt => 0xFFEA,
			KeyCode.LeftMeta => 0xFFEB,
			KeyCode.RightMeta => 0xFFEC,
			KeyCode.NumLock => 0xFF7F,
			KeyCode.NumpadAdd => 0xFFAB,
			KeyCode.NumpadSubtract => 0xFFAD,
			KeyCode.NumpadMultiply => 0xFFAA,
			KeyCode.NumpadDivide => 0xFFAF,
			KeyCode.NumpadDecimal => 0xFFAE,
			KeyCode.NumpadEnter => 0xFF8D,
			KeyCode.MediaPlayPause => 0x1008FF14, // XF86AudioPlay (play/pause toggle)
			KeyCode.MediaStop => 0x1008FF15, // XF86AudioStop
			KeyCode.MediaTrackNext => 0x1008FF17, // XF86AudioNext
			KeyCode.MediaTrackPrevious => 0x1008FF16, // XF86AudioPrev
			KeyCode.AudioVolumeUp => 0x1008FF13, // XF86AudioRaiseVolume
			KeyCode.AudioVolumeDown => 0x1008FF11, // XF86AudioLowerVolume
			KeyCode.AudioVolumeMute => 0x1008FF12, // XF86AudioMute
			_ => 0
		};

		return keysym != 0;
	}

	public void Dispose()
	{
		if (_display != IntPtr.Zero)
		{
			_ = XCloseDisplay(_display);
		}
	}

	[DllImport(LibX11)]
	private static extern int XCloseDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern uint XKeysymToKeycode(IntPtr display, nuint keysym);

	[DllImport(LibX11)]
	private static extern int XFlush(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XDisplayKeycodes(IntPtr display, out int minKeycodes, out int maxKeycodes);

	[DllImport(LibX11)]
	private static extern int XChangeKeyboardMapping(
		IntPtr display,
		int firstKeycode,
		int keysymsPerKeycode,
		nuint[] keysyms,
		int numCodes);

	[DllImport(LibX11)]
	private static extern int XkbGetState(IntPtr display, uint deviceSpec, out XkbState state);

	[DllImport(LibX11)]
	private static extern uint XkbKeysymToModifiers(IntPtr display, nuint keysym);

	[DllImport(LibX11)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool XkbLookupKeySym(
		IntPtr display,
		byte keycode,
		uint modifiers,
		out uint modifiersReturn,
		out nuint keysymReturn);

	[DllImport(LibXtst)]
	private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XSendEvent(
		IntPtr display,
		nuint window,
		[MarshalAs(UnmanagedType.Bool)] bool propagate,
		nint eventMask,
		IntPtr eventPtr);

	[DllImport(LibX11)]
	private static extern int XRaiseWindow(IntPtr display, nuint window);

	[DllImport(LibX11)]
	private static extern int XSetInputFocus(IntPtr display, nuint focus, int revertTo, nuint time);

	private static bool TryGetStateMask(KeyCode key, out uint mask)
	{
		mask = key switch
		{
			KeyCode.LeftShift or KeyCode.RightShift => ShiftMask,
			KeyCode.LeftControl or KeyCode.RightControl => ControlMask,
			KeyCode.LeftAlt or KeyCode.RightAlt => Mod1Mask,
			KeyCode.LeftMeta or KeyCode.RightMeta => Mod4Mask,
			_ => 0u
		};

		return mask != 0;
	}

	private static void SendKeyEvent(IntPtr display, nuint root, nuint window, uint keycode, uint state, bool press)
	{
		var buffer = Marshal.AllocHGlobal(EventBufferSize);
		try
		{
			for (var offset = 0; offset < EventBufferSize; offset += IntPtr.Size)
			{
				Marshal.WriteIntPtr(buffer, offset, IntPtr.Zero);
			}

			Marshal.WriteInt32(buffer, 0, press ? KeyPressType : KeyReleaseType);
			Marshal.WriteInt32(buffer, 16, 1); // send_event = True
			Marshal.WriteIntPtr(buffer, 24, display);
			Marshal.WriteIntPtr(buffer, 32, (nint)window);
			Marshal.WriteIntPtr(buffer, 40, (nint)root);
			Marshal.WriteInt32(buffer, 80, (int)state);
			Marshal.WriteInt32(buffer, 84, (int)keycode);
			Marshal.WriteInt32(buffer, 88, 1); // same_screen = True
			_ = XSendEvent(display, window, false, press ? KeyPressMask : KeyReleaseMask, buffer);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	private static bool ActivateWindow(IntPtr display, nuint root, nuint window)
	{
		var activeAtom = X11Windows.InternAtom(display, "_NET_ACTIVE_WINDOW");
		if (activeAtom == 0)
		{
			return false;
		}

		var buffer = Marshal.AllocHGlobal(EventBufferSize);
		try
		{
			for (var offset = 0; offset < EventBufferSize; offset += IntPtr.Size)
			{
				Marshal.WriteIntPtr(buffer, offset, IntPtr.Zero);
			}

			Marshal.WriteInt32(buffer, 0, ClientMessageType);
			Marshal.WriteInt32(buffer, 16, 1); // send_event = True
			Marshal.WriteIntPtr(buffer, 24, display);
			Marshal.WriteIntPtr(buffer, 32, (nint)window); // target window
			Marshal.WriteIntPtr(buffer, 40, (nint)activeAtom); // message_type
			Marshal.WriteInt32(buffer, 48, 32); // format
			Marshal.WriteIntPtr(buffer, 56, 1); // data.l[0] = source indication: application
			_ = XSendEvent(display, root, false, SubstructureRedirectMask | SubstructureNotifyMask, buffer);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}

		_ = XRaiseWindow(display, window);
		_ = XSetInputFocus(display, window, RevertToParent, 0);
		_ = XFlush(display);
		return true;
	}

	internal readonly record struct TypingKey(uint Keycode, uint Modifiers);

	private readonly record struct TypingLayout(
		uint Group,
		uint ActiveModifiers,
		uint Level3Mask,
		uint ShiftKeycode,
		uint Level3Keycode,
		int MinKeycode,
		int MaxKeycode);

	// Mirrors XkbStateRec from XKBstr.h field for field.
	[StructLayout(LayoutKind.Sequential)]
	private struct XkbState
	{
		public byte Group;
		public byte LockedGroup;
		public ushort BaseGroup;
		public ushort LatchedGroup;
		public byte Mods;
		public byte BaseMods;
		public byte LatchedMods;
		public byte LockedMods;
		public byte CompatState;
		public byte GrabMods;
		public byte CompatGrabMods;
		public byte LookupMods;
		public byte CompatLookupMods;
		public ushort PtrButtons;
	}

	private sealed class LinuxTargetWindow : IKeyboardTargetWindow
	{
		private readonly IntPtr _display;
		private readonly nuint _root;
		private readonly IReadOnlyList<nuint> _windows;
		private readonly Dictionary<KeyCode, uint> _heldMasks = new();

		public LinuxTargetWindow(IntPtr display, nuint root, IReadOnlyList<nuint> windows)
		{
			_display = display;
			_root = root;
			_windows = windows;
		}

		public IDisposable? Focus()
		{
			var previous = X11Windows.GetActiveWindow(_display, _root);
			if (!ActivateWindow(_display, _root, _windows[0]))
			{
				return null;
			}

			Thread.Sleep(40);
			return X11Windows.GetActiveWindow(_display, _root) == _windows[0]
				? new RestoreScope(_display, _root, previous)
				: null;
		}

		public void KeyDown(KeyCode key)
		{
			Send(key, true);
		}

		public void KeyUp(KeyCode key)
		{
			Send(key, false);
		}

		public void TypeUnicode(string text)
		{
			var layout = ReadLayout(_display);
			var groupState = (layout.Group & 3) << XkbGroupShift;
			foreach (var keysym in ToKeysyms(text))
			{
				if (!TryResolveTyping(keysym,
					XKeysymToKeycode(_display, keysym),
					layout.MinKeycode,
					layout.MaxKeycode,
					ShiftMask,
					layout.Level3Mask,
					layout.Group,
					(keycode, state) => LookupKeysym(_display, keycode, state),
					out var key))
				{
					continue;
				}

				var state = key.Modifiers | groupState;
				foreach (var window in _windows)
				{
					SendKeyEvent(_display, _root, window, key.Keycode, state, true);
					SendKeyEvent(_display, _root, window, key.Keycode, state, false);
				}
			}

			_ = XFlush(_display);
		}

		public void Dispose()
		{
		}

		private void Send(KeyCode key, bool press)
		{
			var keycode = ResolveKeycode(key, keysym => XKeysymToKeycode(_display, keysym));
			if (keycode == 0)
			{
				return;
			}

			if (press)
			{
				var mask = ResolveStateMask(key,
					keycode,
					code => LookupKeysym(_display, code, 0),
					keysym => XkbKeysymToModifiers(_display, keysym));
				if (mask != 0)
				{
					_heldMasks[key] = mask;
				}
			}
			else
			{
				_heldMasks.Remove(key);
			}

			var eventState = 0u;
			foreach (var mask in _heldMasks.Values)
			{
				eventState |= mask;
			}

			foreach (var window in _windows)
			{
				SendKeyEvent(_display, _root, window, keycode, eventState, press);
			}

			_ = XFlush(_display);
		}

		private sealed class RestoreScope : IDisposable
		{
			private readonly IntPtr _display;
			private readonly nuint _root;
			private readonly nuint _previous;

			public RestoreScope(IntPtr display, nuint root, nuint previous)
			{
				_display = display;
				_root = root;
				_previous = previous;
			}

			public void Dispose()
			{
				if (_previous != 0)
				{
					_ = ActivateWindow(_display, _root, _previous);
				}
			}
		}
	}
}
