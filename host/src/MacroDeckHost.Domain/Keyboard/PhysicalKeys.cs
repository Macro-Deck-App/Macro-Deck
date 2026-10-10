using MacroDeck.Sdk.Input;

namespace MacroDeckHost.Domain.Keyboard;

public static class PhysicalKeys
{
	// Scan is the PC set-1 scan code, which Windows SendInput takes and which Linux evdev numbers the same
	// way for the main key block. MacKeyCode follows WebKit's kVK mapping, so ISO Macs keep its 10/50 naming.
	private static readonly (KeyCode Key, ushort Scan, ushort MacKeyCode)[] _table =
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

	private static readonly Dictionary<KeyCode, (ushort Scan, ushort MacKeyCode)> _byKey =
		_table.ToDictionary(entry => entry.Key, entry => (entry.Scan, entry.MacKeyCode));

	private static readonly Dictionary<ushort, KeyCode> _byScan = _table.ToDictionary(entry => entry.Scan, entry => entry.Key);

	public static bool IsPositional(KeyCode key) => key is >= KeyCode.Semicolon and <= KeyCode.Quote or KeyCode.IntlBackslash;

	public static bool TryGetScanCode(KeyCode key, out ushort scan)
	{
		var found = _byKey.TryGetValue(key, out var codes);
		scan = codes.Scan;
		return found;
	}

	public static bool TryGetMacKeyCode(KeyCode key, out ushort macKeyCode)
	{
		var found = _byKey.TryGetValue(key, out var codes);
		macKeyCode = codes.MacKeyCode;
		return found;
	}

	public static bool TryFromScanCode(ushort scan, out KeyCode key) => _byScan.TryGetValue(scan, out key);
}
