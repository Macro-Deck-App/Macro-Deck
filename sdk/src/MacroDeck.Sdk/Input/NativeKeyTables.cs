namespace MacroDeck.Sdk.Input;

internal static class NativeKeyTables
{
	// Scan is the PC set-1 scan code, which Windows reports and Linux evdev numbers the same way for the
	// main key block. MacKeyCode follows WebKit's kVK mapping, so ISO Macs keep its 10/50 naming.
	private static readonly (KeyCode Key, ushort Scan, ushort MacKeyCode)[] _positions =
	[
		(KeyCode.A, 0x1E, 0), (KeyCode.B, 0x30, 11), (KeyCode.C, 0x2E, 8), (KeyCode.D, 0x20, 2),
		(KeyCode.E, 0x12, 14), (KeyCode.F, 0x21, 3), (KeyCode.G, 0x22, 5), (KeyCode.H, 0x23, 4),
		(KeyCode.I, 0x17, 34), (KeyCode.J, 0x24, 38), (KeyCode.K, 0x25, 40), (KeyCode.L, 0x26, 37),
		(KeyCode.M, 0x32, 46), (KeyCode.N, 0x31, 45), (KeyCode.O, 0x18, 31), (KeyCode.P, 0x19, 35),
		(KeyCode.Q, 0x10, 12), (KeyCode.R, 0x13, 15), (KeyCode.S, 0x1F, 1), (KeyCode.T, 0x14, 17),
		(KeyCode.U, 0x16, 32), (KeyCode.V, 0x2F, 9), (KeyCode.W, 0x11, 13), (KeyCode.X, 0x2D, 7),
		(KeyCode.Y, 0x15, 16), (KeyCode.Z, 0x2C, 6),

		(KeyCode.D1, 0x02, 18), (KeyCode.D2, 0x03, 19), (KeyCode.D3, 0x04, 20), (KeyCode.D4, 0x05, 21),
		(KeyCode.D5, 0x06, 23), (KeyCode.D6, 0x07, 22), (KeyCode.D7, 0x08, 26), (KeyCode.D8, 0x09, 28),
		(KeyCode.D9, 0x0A, 25), (KeyCode.D0, 0x0B, 29),

		(KeyCode.Minus, 0x0C, 27), (KeyCode.Equal, 0x0D, 24), (KeyCode.BracketLeft, 0x1A, 33),
		(KeyCode.BracketRight, 0x1B, 30), (KeyCode.Backslash, 0x2B, 42), (KeyCode.Semicolon, 0x27, 41),
		(KeyCode.Quote, 0x28, 39), (KeyCode.Backquote, 0x29, 50), (KeyCode.Comma, 0x33, 43),
		(KeyCode.Period, 0x34, 47), (KeyCode.Slash, 0x35, 44), (KeyCode.IntlBackslash, 0x56, 10)
	];

	private static readonly Dictionary<int, KeyCode> _byScan = _positions.ToDictionary(p => (int)p.Scan, p => p.Key);

	private static readonly Dictionary<int, KeyCode> _byMacKeyCode = _positions.ToDictionary(p => (int)p.MacKeyCode, p => p.Key);

	private static readonly Dictionary<int, KeyCode> _windowsPunctuation = new()
	{
		[0xBA] = KeyCode.Semicolon, [0xBB] = KeyCode.Equal, [0xBC] = KeyCode.Comma, [0xBD] = KeyCode.Minus,
		[0xBE] = KeyCode.Period, [0xBF] = KeyCode.Slash, [0xC0] = KeyCode.Backquote, [0xDB] = KeyCode.BracketLeft,
		[0xDC] = KeyCode.Backslash, [0xDD] = KeyCode.BracketRight, [0xDE] = KeyCode.Quote,
		[0xE2] = KeyCode.IntlBackslash
	};

	private static readonly Dictionary<int, KeyCode> _windowsKeys = new()
	{
		[0x08] = KeyCode.Backspace, [0x09] = KeyCode.Tab, [0x13] = KeyCode.Pause, [0x14] = KeyCode.CapsLock,
		[0x1B] = KeyCode.Escape, [0x20] = KeyCode.Space, [0x2C] = KeyCode.PrintScreen,
		[0x5B] = KeyCode.LeftMeta, [0x5C] = KeyCode.RightMeta,
		[0x6A] = KeyCode.NumpadMultiply, [0x6B] = KeyCode.NumpadAdd, [0x6D] = KeyCode.NumpadSubtract,
		[0x6E] = KeyCode.NumpadDecimal, [0x6F] = KeyCode.NumpadDivide,
		[0x90] = KeyCode.NumLock, [0x91] = KeyCode.ScrollLock,
		[0xA0] = KeyCode.LeftShift, [0xA1] = KeyCode.RightShift, [0xA2] = KeyCode.LeftControl,
		[0xA3] = KeyCode.RightControl, [0xA4] = KeyCode.LeftAlt, [0xA5] = KeyCode.RightAlt,
		[0xAD] = KeyCode.AudioVolumeMute, [0xAE] = KeyCode.AudioVolumeDown, [0xAF] = KeyCode.AudioVolumeUp,
		[0xB0] = KeyCode.MediaTrackNext, [0xB1] = KeyCode.MediaTrackPrevious, [0xB2] = KeyCode.MediaStop,
		[0xB3] = KeyCode.MediaPlayPause
	};

	// Virtual key -> (key when extended, key when not extended). Without NumLock the numpad reports the
	// navigation virtual keys without the extended flag.
	private static readonly Dictionary<int, (KeyCode Extended, KeyCode Numpad)> _windowsNavigation = new()
	{
		[0x0C] = (KeyCode.Numpad5, KeyCode.Numpad5),
		[0x21] = (KeyCode.PageUp, KeyCode.Numpad9), [0x22] = (KeyCode.PageDown, KeyCode.Numpad3),
		[0x23] = (KeyCode.End, KeyCode.Numpad1), [0x24] = (KeyCode.Home, KeyCode.Numpad7),
		[0x25] = (KeyCode.ArrowLeft, KeyCode.Numpad4), [0x26] = (KeyCode.ArrowUp, KeyCode.Numpad8),
		[0x27] = (KeyCode.ArrowRight, KeyCode.Numpad6), [0x28] = (KeyCode.ArrowDown, KeyCode.Numpad2),
		[0x2D] = (KeyCode.Insert, KeyCode.Numpad0), [0x2E] = (KeyCode.Delete, KeyCode.NumpadDecimal)
	};

	private static readonly Dictionary<int, KeyCode> _macKeys = new()
	{
		[36] = KeyCode.Enter, [48] = KeyCode.Tab, [49] = KeyCode.Space, [51] = KeyCode.Backspace,
		[53] = KeyCode.Escape, [57] = KeyCode.CapsLock,
		[55] = KeyCode.LeftMeta, [54] = KeyCode.RightMeta, [56] = KeyCode.LeftShift, [60] = KeyCode.RightShift,
		[58] = KeyCode.LeftAlt, [61] = KeyCode.RightAlt, [59] = KeyCode.LeftControl, [62] = KeyCode.RightControl,
		[122] = KeyCode.F1, [120] = KeyCode.F2, [99] = KeyCode.F3, [118] = KeyCode.F4, [96] = KeyCode.F5,
		[97] = KeyCode.F6, [98] = KeyCode.F7, [100] = KeyCode.F8, [101] = KeyCode.F9, [109] = KeyCode.F10,
		[103] = KeyCode.F11, [111] = KeyCode.F12, [105] = KeyCode.F13, [107] = KeyCode.F14, [113] = KeyCode.F15,
		[106] = KeyCode.F16, [64] = KeyCode.F17, [79] = KeyCode.F18, [80] = KeyCode.F19, [90] = KeyCode.F20,
		[115] = KeyCode.Home, [119] = KeyCode.End, [116] = KeyCode.PageUp, [121] = KeyCode.PageDown,
		[117] = KeyCode.Delete, [114] = KeyCode.Insert,
		[123] = KeyCode.ArrowLeft, [124] = KeyCode.ArrowRight, [125] = KeyCode.ArrowDown, [126] = KeyCode.ArrowUp,
		[82] = KeyCode.Numpad0, [83] = KeyCode.Numpad1, [84] = KeyCode.Numpad2, [85] = KeyCode.Numpad3,
		[86] = KeyCode.Numpad4, [87] = KeyCode.Numpad5, [88] = KeyCode.Numpad6, [89] = KeyCode.Numpad7,
		[91] = KeyCode.Numpad8, [92] = KeyCode.Numpad9,
		[65] = KeyCode.NumpadDecimal, [67] = KeyCode.NumpadMultiply, [69] = KeyCode.NumpadAdd,
		[75] = KeyCode.NumpadDivide, [76] = KeyCode.NumpadEnter, [78] = KeyCode.NumpadSubtract
	};

	private static readonly Dictionary<int, KeyCode> _linuxKeys = new()
	{
		[1] = KeyCode.Escape, [14] = KeyCode.Backspace, [15] = KeyCode.Tab, [28] = KeyCode.Enter,
		[29] = KeyCode.LeftControl, [42] = KeyCode.LeftShift, [54] = KeyCode.RightShift, [55] = KeyCode.NumpadMultiply,
		[56] = KeyCode.LeftAlt, [57] = KeyCode.Space, [58] = KeyCode.CapsLock,
		[59] = KeyCode.F1, [60] = KeyCode.F2, [61] = KeyCode.F3, [62] = KeyCode.F4, [63] = KeyCode.F5,
		[64] = KeyCode.F6, [65] = KeyCode.F7, [66] = KeyCode.F8, [67] = KeyCode.F9, [68] = KeyCode.F10,
		[87] = KeyCode.F11, [88] = KeyCode.F12,
		[69] = KeyCode.NumLock, [70] = KeyCode.ScrollLock,
		[71] = KeyCode.Numpad7, [72] = KeyCode.Numpad8, [73] = KeyCode.Numpad9, [74] = KeyCode.NumpadSubtract,
		[75] = KeyCode.Numpad4, [76] = KeyCode.Numpad5, [77] = KeyCode.Numpad6, [78] = KeyCode.NumpadAdd,
		[79] = KeyCode.Numpad1, [80] = KeyCode.Numpad2, [81] = KeyCode.Numpad3, [82] = KeyCode.Numpad0,
		[83] = KeyCode.NumpadDecimal, [96] = KeyCode.NumpadEnter, [98] = KeyCode.NumpadDivide,
		[97] = KeyCode.RightControl, [99] = KeyCode.PrintScreen, [100] = KeyCode.RightAlt,
		[102] = KeyCode.Home, [103] = KeyCode.ArrowUp, [104] = KeyCode.PageUp, [105] = KeyCode.ArrowLeft,
		[106] = KeyCode.ArrowRight, [107] = KeyCode.End, [108] = KeyCode.ArrowDown, [109] = KeyCode.PageDown,
		[110] = KeyCode.Insert, [111] = KeyCode.Delete,
		[113] = KeyCode.AudioVolumeMute, [114] = KeyCode.AudioVolumeDown, [115] = KeyCode.AudioVolumeUp,
		[119] = KeyCode.Pause, [125] = KeyCode.LeftMeta, [126] = KeyCode.RightMeta,
		[163] = KeyCode.MediaTrackNext, [164] = KeyCode.MediaPlayPause, [165] = KeyCode.MediaTrackPrevious,
		[166] = KeyCode.MediaStop
	};

	public static KeyCode FromWindows(int virtualKey, int scanCode, bool isExtended, Func<int, int> scanCodeOf)
	{
		switch (virtualKey)
		{
			case >= 0x41 and <= 0x5A:
				return KeyCode.A + (virtualKey - 0x41);
			case >= 0x30 and <= 0x39:
				return KeyCode.D0 + (virtualKey - 0x30);
			case >= 0x60 and <= 0x69:
				return KeyCode.Numpad0 + (virtualKey - 0x60);
			case >= 0x70 and <= 0x7B:
				return KeyCode.F1 + (virtualKey - 0x70);
			case >= 0x7C and <= 0x87:
				return KeyCode.F13 + (virtualKey - 0x7C);
			case 0x0D:
				return isExtended ? KeyCode.NumpadEnter : KeyCode.Enter;
			case 0x10:
				return KeyCode.LeftShift;
			case 0x11:
				return isExtended ? KeyCode.RightControl : KeyCode.LeftControl;
			case 0x12:
				return isExtended ? KeyCode.RightAlt : KeyCode.LeftAlt;
		}

		if (_windowsNavigation.TryGetValue(virtualKey, out var navigation))
		{
			return isExtended ? navigation.Extended : navigation.Numpad;
		}

		if (_windowsKeys.TryGetValue(virtualKey, out var key))
		{
			return key;
		}

		return _windowsPunctuation.ContainsKey(virtualKey) || virtualKey == 0xDF
			? FromWindowsPunctuation(virtualKey, scanCode, scanCodeOf)
			: KeyCode.None;
	}

	public static KeyCode FromMacOS(int keyCode, Func<ushort, string?> translate)
	{
		if (keyCode is < 0 or > 127)
		{
			return KeyCode.None;
		}

		if (_macKeys.TryGetValue(keyCode, out var fixedKey))
		{
			return fixedKey;
		}

		if (translate((ushort)keyCode) is [var character] && char.ToUpperInvariant(character) is >= 'A' and <= 'Z' and var upper)
		{
			return KeyCode.A + (upper - 'A');
		}

		return _byMacKeyCode.GetValueOrDefault(keyCode);
	}

	public static KeyCode FromLinux(int evdevCode)
		=> evdevCode is >= 183 and <= 194
			? KeyCode.F13 + (evdevCode - 183)
			: _linuxKeys.TryGetValue(evdevCode, out var key) ? key : _byScan.GetValueOrDefault(evdevCode);

	private static KeyCode FromWindowsPunctuation(int virtualKey, int scanCode, Func<int, int> scanCodeOf)
	{
		if (_byScan.TryGetValue(scanCode, out var key))
		{
			return key;
		}

		var layoutScan = scanCodeOf(virtualKey);
		if (_byScan.TryGetValue(layoutScan, out key))
		{
			return key;
		}

		return _windowsPunctuation.GetValueOrDefault(virtualKey);
	}
}
