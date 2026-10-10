using MacroDeck.Sdk.Input;

namespace MacroDeck.Sdk.Tests.UnitTests.Input;

[TestFixture]
public class NativeKeysTests
{
	private static int NoLayout(int virtualKey) => 0;

	private static KeyCode Windows(int virtualKey, bool extended = false)
		=> NativeKeyTables.FromWindows(virtualKey, 0, extended, NoLayout);

	[TestCase(0x41, KeyCode.A)]
	[TestCase(0x5A, KeyCode.Z)]
	[TestCase(0x30, KeyCode.D0)]
	[TestCase(0x39, KeyCode.D9)]
	[TestCase(0x70, KeyCode.F1)]
	[TestCase(0x7B, KeyCode.F12)]
	[TestCase(0x7C, KeyCode.F13)]
	[TestCase(0x87, KeyCode.F24)]
	[TestCase(0x60, KeyCode.Numpad0)]
	[TestCase(0x69, KeyCode.Numpad9)]
	[TestCase(0x6A, KeyCode.NumpadMultiply)]
	[TestCase(0x6B, KeyCode.NumpadAdd)]
	[TestCase(0x6D, KeyCode.NumpadSubtract)]
	[TestCase(0x6E, KeyCode.NumpadDecimal)]
	[TestCase(0x6F, KeyCode.NumpadDivide)]
	[TestCase(0x08, KeyCode.Backspace)]
	[TestCase(0x09, KeyCode.Tab)]
	[TestCase(0x13, KeyCode.Pause)]
	[TestCase(0x14, KeyCode.CapsLock)]
	[TestCase(0x1B, KeyCode.Escape)]
	[TestCase(0x20, KeyCode.Space)]
	[TestCase(0x2C, KeyCode.PrintScreen)]
	[TestCase(0x90, KeyCode.NumLock)]
	[TestCase(0x91, KeyCode.ScrollLock)]
	[TestCase(0xA0, KeyCode.LeftShift)]
	[TestCase(0xA1, KeyCode.RightShift)]
	[TestCase(0xA2, KeyCode.LeftControl)]
	[TestCase(0xA3, KeyCode.RightControl)]
	[TestCase(0xA4, KeyCode.LeftAlt)]
	[TestCase(0xA5, KeyCode.RightAlt)]
	[TestCase(0x5B, KeyCode.LeftMeta)]
	[TestCase(0x5C, KeyCode.RightMeta)]
	[TestCase(0xAD, KeyCode.AudioVolumeMute)]
	[TestCase(0xAE, KeyCode.AudioVolumeDown)]
	[TestCase(0xAF, KeyCode.AudioVolumeUp)]
	[TestCase(0xB0, KeyCode.MediaTrackNext)]
	[TestCase(0xB1, KeyCode.MediaTrackPrevious)]
	[TestCase(0xB2, KeyCode.MediaStop)]
	[TestCase(0xB3, KeyCode.MediaPlayPause)]
	public void A_windows_virtual_key_is_the_key_it_names(int virtualKey, KeyCode expected)
		=> Assert.That(Windows(virtualKey), Is.EqualTo(expected));

	[Test]
	public void The_extended_flag_tells_the_numpad_enter_from_enter()
		=> Assert.Multiple(() =>
		{
			Assert.That(Windows(0x0D, extended: false), Is.EqualTo(KeyCode.Enter));
			Assert.That(Windows(0x0D, extended: true), Is.EqualTo(KeyCode.NumpadEnter));
		});

	[Test]
	public void The_generic_modifier_virtual_keys_map_to_the_left_key_unless_extended()
		=> Assert.Multiple(() =>
		{
			Assert.That(Windows(0x10), Is.EqualTo(KeyCode.LeftShift));
			Assert.That(Windows(0x10, extended: true), Is.EqualTo(KeyCode.LeftShift));
			Assert.That(Windows(0x11), Is.EqualTo(KeyCode.LeftControl));
			Assert.That(Windows(0x11, extended: true), Is.EqualTo(KeyCode.RightControl));
			Assert.That(Windows(0x12), Is.EqualTo(KeyCode.LeftAlt));
			Assert.That(Windows(0x12, extended: true), Is.EqualTo(KeyCode.RightAlt));
		});

	[TestCase(0x21, KeyCode.PageUp, KeyCode.Numpad9)]
	[TestCase(0x22, KeyCode.PageDown, KeyCode.Numpad3)]
	[TestCase(0x23, KeyCode.End, KeyCode.Numpad1)]
	[TestCase(0x24, KeyCode.Home, KeyCode.Numpad7)]
	[TestCase(0x25, KeyCode.ArrowLeft, KeyCode.Numpad4)]
	[TestCase(0x26, KeyCode.ArrowUp, KeyCode.Numpad8)]
	[TestCase(0x27, KeyCode.ArrowRight, KeyCode.Numpad6)]
	[TestCase(0x28, KeyCode.ArrowDown, KeyCode.Numpad2)]
	[TestCase(0x2D, KeyCode.Insert, KeyCode.Numpad0)]
	[TestCase(0x2E, KeyCode.Delete, KeyCode.NumpadDecimal)]
	public void A_navigation_virtual_key_without_the_extended_flag_is_the_numpad_key(int virtualKey, KeyCode navigation, KeyCode numpad)
		=> Assert.Multiple(() =>
		{
			Assert.That(Windows(virtualKey, extended: true), Is.EqualTo(navigation));
			Assert.That(Windows(virtualKey, extended: false), Is.EqualTo(numpad));
		});

	[Test]
	public void Clear_is_the_numpad_five()
		=> Assert.That(Windows(0x0C), Is.EqualTo(KeyCode.Numpad5));

	[TestCase(0xBA, KeyCode.Semicolon)]
	[TestCase(0xBB, KeyCode.Equal)]
	[TestCase(0xBC, KeyCode.Comma)]
	[TestCase(0xBD, KeyCode.Minus)]
	[TestCase(0xBE, KeyCode.Period)]
	[TestCase(0xBF, KeyCode.Slash)]
	[TestCase(0xC0, KeyCode.Backquote)]
	[TestCase(0xDB, KeyCode.BracketLeft)]
	[TestCase(0xDC, KeyCode.Backslash)]
	[TestCase(0xDD, KeyCode.BracketRight)]
	[TestCase(0xDE, KeyCode.Quote)]
	[TestCase(0xE2, KeyCode.IntlBackslash)]
	public void Without_a_layout_a_punctuation_virtual_key_is_the_us_position(int virtualKey, KeyCode expected)
		=> Assert.That(Windows(virtualKey), Is.EqualTo(expected));

	[Test]
	public void On_a_german_layout_the_sharp_s_virtual_key_is_the_minus_position()
	{
		var german = (int virtualKey) => virtualKey == 0xDB ? 0x0C : 0;

		Assert.That(NativeKeyTables.FromWindows(0xDB, 0, false, german), Is.EqualTo(KeyCode.Minus));
	}

	[Test]
	public void A_scan_code_names_the_position_without_asking_the_layout()
	{
		Func<int, int> failing = _ => throw new InvalidOperationException("The layout must not be asked.");

		Assert.Multiple(() =>
		{
			Assert.That(NativeKeyTables.FromWindows(0xDB, 0x0C, false, failing), Is.EqualTo(KeyCode.Minus));
			Assert.That(NativeKeyTables.FromWindows(0xE2, 0x56, false, failing), Is.EqualTo(KeyCode.IntlBackslash));
		});
	}

	[Test]
	public void An_unknown_scan_code_falls_back_to_the_layout_and_then_to_the_us_position()
	{
		var german = (int virtualKey) => virtualKey == 0xDB ? 0x0C : 0;

		Assert.Multiple(() =>
		{
			Assert.That(NativeKeyTables.FromWindows(0xDB, 0x7D, false, german), Is.EqualTo(KeyCode.Minus));
			Assert.That(NativeKeyTables.FromWindows(0xE2, 0x7D, false, NoLayout), Is.EqualTo(KeyCode.IntlBackslash));
		});
	}

	[Test]
	public void The_eighth_oem_virtual_key_is_known_only_through_its_position()
		=> Assert.Multiple(() =>
		{
			Assert.That(NativeKeyTables.FromWindows(0xDF, 0x29, false, NoLayout), Is.EqualTo(KeyCode.Backquote));
			Assert.That(Windows(0xDF), Is.EqualTo(KeyCode.None));
		});

	[TestCase(0x5D)]
	[TestCase(0xE7)]
	[TestCase(0xE5)]
	[TestCase(0)]
	[TestCase(-1)]
	[TestCase(100000)]
	public void A_windows_virtual_key_without_a_key_code_is_none(int virtualKey)
		=> Assert.That(Windows(virtualKey), Is.EqualTo(KeyCode.None));

	[Test]
	public void Windows_letters_and_digits_do_not_need_a_layout()
		=> Assert.Multiple(() =>
		{
			Assert.That(NativeKeys.FromWindows(0x5A, false), Is.EqualTo(KeyCode.Z));
			Assert.That(NativeKeys.FromWindows(0x31, 0x02, false), Is.EqualTo(KeyCode.D1));
		});

	[TestCase(30, KeyCode.A)]
	[TestCase(44, KeyCode.Z)]
	[TestCase(16, KeyCode.Q)]
	[TestCase(2, KeyCode.D1)]
	[TestCase(11, KeyCode.D0)]
	[TestCase(12, KeyCode.Minus)]
	[TestCase(13, KeyCode.Equal)]
	[TestCase(26, KeyCode.BracketLeft)]
	[TestCase(27, KeyCode.BracketRight)]
	[TestCase(43, KeyCode.Backslash)]
	[TestCase(39, KeyCode.Semicolon)]
	[TestCase(40, KeyCode.Quote)]
	[TestCase(41, KeyCode.Backquote)]
	[TestCase(51, KeyCode.Comma)]
	[TestCase(52, KeyCode.Period)]
	[TestCase(53, KeyCode.Slash)]
	[TestCase(86, KeyCode.IntlBackslash)]
	[TestCase(1, KeyCode.Escape)]
	[TestCase(14, KeyCode.Backspace)]
	[TestCase(15, KeyCode.Tab)]
	[TestCase(28, KeyCode.Enter)]
	[TestCase(96, KeyCode.NumpadEnter)]
	[TestCase(57, KeyCode.Space)]
	[TestCase(58, KeyCode.CapsLock)]
	[TestCase(59, KeyCode.F1)]
	[TestCase(68, KeyCode.F10)]
	[TestCase(87, KeyCode.F11)]
	[TestCase(88, KeyCode.F12)]
	[TestCase(183, KeyCode.F13)]
	[TestCase(194, KeyCode.F24)]
	[TestCase(69, KeyCode.NumLock)]
	[TestCase(70, KeyCode.ScrollLock)]
	[TestCase(99, KeyCode.PrintScreen)]
	[TestCase(119, KeyCode.Pause)]
	[TestCase(102, KeyCode.Home)]
	[TestCase(107, KeyCode.End)]
	[TestCase(104, KeyCode.PageUp)]
	[TestCase(109, KeyCode.PageDown)]
	[TestCase(110, KeyCode.Insert)]
	[TestCase(111, KeyCode.Delete)]
	[TestCase(103, KeyCode.ArrowUp)]
	[TestCase(108, KeyCode.ArrowDown)]
	[TestCase(105, KeyCode.ArrowLeft)]
	[TestCase(106, KeyCode.ArrowRight)]
	[TestCase(82, KeyCode.Numpad0)]
	[TestCase(71, KeyCode.Numpad7)]
	[TestCase(73, KeyCode.Numpad9)]
	[TestCase(55, KeyCode.NumpadMultiply)]
	[TestCase(78, KeyCode.NumpadAdd)]
	[TestCase(74, KeyCode.NumpadSubtract)]
	[TestCase(98, KeyCode.NumpadDivide)]
	[TestCase(83, KeyCode.NumpadDecimal)]
	[TestCase(29, KeyCode.LeftControl)]
	[TestCase(97, KeyCode.RightControl)]
	[TestCase(42, KeyCode.LeftShift)]
	[TestCase(54, KeyCode.RightShift)]
	[TestCase(56, KeyCode.LeftAlt)]
	[TestCase(100, KeyCode.RightAlt)]
	[TestCase(125, KeyCode.LeftMeta)]
	[TestCase(126, KeyCode.RightMeta)]
	[TestCase(113, KeyCode.AudioVolumeMute)]
	[TestCase(114, KeyCode.AudioVolumeDown)]
	[TestCase(115, KeyCode.AudioVolumeUp)]
	[TestCase(163, KeyCode.MediaTrackNext)]
	[TestCase(164, KeyCode.MediaPlayPause)]
	[TestCase(165, KeyCode.MediaTrackPrevious)]
	[TestCase(166, KeyCode.MediaStop)]
	public void A_linux_evdev_code_is_the_key_it_names(int evdevCode, KeyCode expected)
		=> Assert.That(NativeKeys.FromLinux(evdevCode), Is.EqualTo(expected));

	[Test]
	public void Linux_letters_are_us_positions_whatever_the_layout()
		=> Assert.That(NativeKeys.FromLinux(21), Is.EqualTo(KeyCode.Y));

	[TestCase(0)]
	[TestCase(-1)]
	[TestCase(255)]
	[TestCase(100000)]
	public void A_linux_code_without_a_key_code_is_none(int evdevCode)
		=> Assert.That(NativeKeys.FromLinux(evdevCode), Is.EqualTo(KeyCode.None));

	private static KeyCode Mac(int keyCode, Dictionary<ushort, string>? layout = null)
		=> NativeKeyTables.FromMacOS(keyCode, code => layout?.GetValueOrDefault(code));

	[TestCase(36, KeyCode.Enter)]
	[TestCase(76, KeyCode.NumpadEnter)]
	[TestCase(48, KeyCode.Tab)]
	[TestCase(49, KeyCode.Space)]
	[TestCase(51, KeyCode.Backspace)]
	[TestCase(53, KeyCode.Escape)]
	[TestCase(57, KeyCode.CapsLock)]
	[TestCase(55, KeyCode.LeftMeta)]
	[TestCase(54, KeyCode.RightMeta)]
	[TestCase(56, KeyCode.LeftShift)]
	[TestCase(60, KeyCode.RightShift)]
	[TestCase(58, KeyCode.LeftAlt)]
	[TestCase(61, KeyCode.RightAlt)]
	[TestCase(59, KeyCode.LeftControl)]
	[TestCase(62, KeyCode.RightControl)]
	[TestCase(122, KeyCode.F1)]
	[TestCase(111, KeyCode.F12)]
	[TestCase(105, KeyCode.F13)]
	[TestCase(90, KeyCode.F20)]
	[TestCase(115, KeyCode.Home)]
	[TestCase(119, KeyCode.End)]
	[TestCase(116, KeyCode.PageUp)]
	[TestCase(121, KeyCode.PageDown)]
	[TestCase(117, KeyCode.Delete)]
	[TestCase(114, KeyCode.Insert)]
	[TestCase(126, KeyCode.ArrowUp)]
	[TestCase(125, KeyCode.ArrowDown)]
	[TestCase(123, KeyCode.ArrowLeft)]
	[TestCase(124, KeyCode.ArrowRight)]
	[TestCase(82, KeyCode.Numpad0)]
	[TestCase(92, KeyCode.Numpad9)]
	[TestCase(65, KeyCode.NumpadDecimal)]
	[TestCase(67, KeyCode.NumpadMultiply)]
	[TestCase(69, KeyCode.NumpadAdd)]
	[TestCase(75, KeyCode.NumpadDivide)]
	[TestCase(78, KeyCode.NumpadSubtract)]
	public void A_mac_key_code_that_types_no_character_is_the_key_it_names(int keyCode, KeyCode expected)
		=> Assert.That(Mac(keyCode), Is.EqualTo(expected));

	[TestCase(0, KeyCode.A)]
	[TestCase(6, KeyCode.Z)]
	[TestCase(18, KeyCode.D1)]
	[TestCase(29, KeyCode.D0)]
	[TestCase(27, KeyCode.Minus)]
	[TestCase(24, KeyCode.Equal)]
	[TestCase(33, KeyCode.BracketLeft)]
	[TestCase(30, KeyCode.BracketRight)]
	[TestCase(42, KeyCode.Backslash)]
	[TestCase(41, KeyCode.Semicolon)]
	[TestCase(39, KeyCode.Quote)]
	[TestCase(43, KeyCode.Comma)]
	[TestCase(47, KeyCode.Period)]
	[TestCase(44, KeyCode.Slash)]
	[TestCase(50, KeyCode.Backquote)]
	[TestCase(10, KeyCode.IntlBackslash)]
	public void Without_a_layout_a_mac_key_code_is_the_us_position(int keyCode, KeyCode expected)
		=> Assert.That(Mac(keyCode), Is.EqualTo(expected));

	[Test]
	public void On_a_german_layout_the_key_labeled_z_is_z_and_the_sharp_s_key_is_the_minus_position()
	{
		var german = new Dictionary<ushort, string> { [6] = "y", [16] = "z", [27] = "ß", [33] = "ü" };

		Assert.Multiple(() =>
		{
			Assert.That(Mac(6, german), Is.EqualTo(KeyCode.Y));
			Assert.That(Mac(16, german), Is.EqualTo(KeyCode.Z));
			Assert.That(Mac(27, german), Is.EqualTo(KeyCode.Minus));
			Assert.That(Mac(33, german), Is.EqualTo(KeyCode.BracketLeft));
		});
	}

	[Test]
	public void On_a_french_layout_the_letters_follow_the_labels()
	{
		var french = new Dictionary<ushort, string> { [12] = "a", [0] = "q", [13] = "z", [6] = "w", [41] = "m", [46] = "," };

		Assert.Multiple(() =>
		{
			Assert.That(Mac(12, french), Is.EqualTo(KeyCode.A));
			Assert.That(Mac(0, french), Is.EqualTo(KeyCode.Q));
			Assert.That(Mac(13, french), Is.EqualTo(KeyCode.Z));
			Assert.That(Mac(6, french), Is.EqualTo(KeyCode.W));
			Assert.That(Mac(41, french), Is.EqualTo(KeyCode.M));
			Assert.That(Mac(46, french), Is.EqualTo(KeyCode.M));
		});
	}

	[Test]
	public void A_layout_without_latin_letters_keeps_the_us_letter_of_the_position()
	{
		var russian = new Dictionary<ushort, string> { [0] = "ф", [6] = "я" };
		var dvorak = new Dictionary<ushort, string> { [6] = ";", [44] = "z" };

		Assert.Multiple(() =>
		{
			Assert.That(Mac(0, russian), Is.EqualTo(KeyCode.A));
			Assert.That(Mac(6, russian), Is.EqualTo(KeyCode.Z));
			Assert.That(Mac(6, dvorak), Is.EqualTo(KeyCode.Z));
			Assert.That(Mac(44, dvorak), Is.EqualTo(KeyCode.Z));
		});
	}

	[Test]
	public void A_typed_upper_case_letter_names_the_same_key()
		=> Assert.That(Mac(6, new Dictionary<ushort, string> { [6] = "Y" }), Is.EqualTo(KeyCode.Y));

	[Test]
	public void A_translation_of_several_characters_is_no_letter()
		=> Assert.That(Mac(6, new Dictionary<ushort, string> { [6] = "yy" }), Is.EqualTo(KeyCode.Z));

	[TestCase(63)]
	[TestCase(-1)]
	[TestCase(128)]
	[TestCase(100000)]
	public void A_mac_key_code_without_a_key_code_is_none(int keyCode)
		=> Assert.That(Mac(keyCode), Is.EqualTo(KeyCode.None));
}
