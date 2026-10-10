using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyVocabularyTests
{
	private static readonly KeyCode[] _positionalKeys =
	[
		KeyCode.Minus, KeyCode.Equal, KeyCode.BracketLeft, KeyCode.BracketRight, KeyCode.Backslash,
		KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma, KeyCode.Period, KeyCode.Slash, KeyCode.Backquote,
		KeyCode.IntlBackslash
	];

	[TestCase("-", KeyCode.Minus)]
	[TestCase("minus", KeyCode.Minus)]
	[TestCase("[", KeyCode.BracketLeft)]
	[TestCase("IntlBackslash", KeyCode.IntlBackslash)]
	[TestCase("Esc", KeyCode.Escape)]
	[TestCase("7", KeyCode.D7)]
	[TestCase(" Return ", KeyCode.Enter)]
	public void Every_spelling_the_editor_or_a_plugin_has_stored_resolves_to_one_key(string name, KeyCode expected)
	{
		Assert.That(KeyNames.TryParseKey(name, out var key), Is.True);
		Assert.That(key, Is.EqualTo(expected));
	}

	[TestCase("_")]
	[TestCase("SS")]
	[TestCase("Ü")]
	[TestCase("<")]
	[TestCase("")]
	public void Characters_that_only_name_a_key_on_one_layout_do_not_resolve(string name)
		=> Assert.That(KeyNames.TryParseKey(name, out _), Is.False);

	[TestCase("Cmd", KeyModifier.Meta)]
	[TestCase("Win", KeyModifier.Meta)]
	[TestCase("Super", KeyModifier.Meta)]
	[TestCase("Option", KeyModifier.Alt)]
	[TestCase("AltGr", KeyModifier.Alt)]
	[TestCase("Control", KeyModifier.Control)]
	[TestCase("RightCtrl", KeyModifier.RightControl)]
	public void Modifier_aliases_resolve_to_one_modifier(string name, KeyModifier expected)
	{
		Assert.That(KeyNames.TryParseModifier(name, out var modifier), Is.True);
		Assert.That(modifier, Is.EqualTo(expected));
	}

	[Test]
	public void Every_positional_key_has_a_position_on_every_platform()
	{
		Assert.Multiple(() =>
		{
			foreach (var key in _positionalKeys)
			{
				Assert.That(PhysicalKeys.IsPositional(key), Is.True, key.ToString());
				Assert.That(PhysicalKeys.TryGetScanCode(key, out var scan), Is.True, key.ToString());
				Assert.That(PhysicalKeys.TryGetMacKeyCode(key, out _), Is.True, key.ToString());
				Assert.That(PhysicalKeys.TryFromScanCode(scan, out var back) ? back : KeyCode.None, Is.EqualTo(key));
			}
		});
	}

	[Test]
	public void Letters_digits_and_named_keys_are_not_positional()
		=> Assert.That(new[] { KeyCode.A, KeyCode.Z, KeyCode.D1, KeyCode.Enter, KeyCode.Numpad1 }
			.Where(PhysicalKeys.IsPositional), Is.Empty);

	[TestCase(KeyCode.Minus, 0x0C, 27)]
	[TestCase(KeyCode.BracketLeft, 0x1A, 33)]
	[TestCase(KeyCode.Backquote, 0x29, 50)]
	[TestCase(KeyCode.IntlBackslash, 0x56, 10)]
	[TestCase(KeyCode.Z, 0x2C, 6)]
	[TestCase(KeyCode.Q, 0x10, 12)]
	public void Physical_positions_are_the_us_keyboard_positions(KeyCode key, int scan, int macKeyCode)
	{
		PhysicalKeys.TryGetScanCode(key, out var actualScan);
		PhysicalKeys.TryGetMacKeyCode(key, out var actualMac);
		Assert.Multiple(() =>
		{
			Assert.That(actualScan, Is.EqualTo(scan));
			Assert.That(actualMac, Is.EqualTo(macKeyCode));
		});
	}

	[Test]
	public void The_mac_letter_map_follows_the_letters_the_layout_types()
	{
		var german = new Dictionary<ushort, string> { [6] = "y", [16] = "z", [0] = "a", [27] = "ß", [33] = "ü" };

		var letters = MacOsKeyboardLayout.BuildLetterMap(keyCode => german.GetValueOrDefault(keyCode));

		Assert.Multiple(() =>
		{
			Assert.That(letters[KeyCode.Z], Is.EqualTo(16));
			Assert.That(letters[KeyCode.Y], Is.EqualTo(6));
			Assert.That(letters[KeyCode.A], Is.EqualTo(0));
			Assert.That(letters, Has.Count.EqualTo(3));
		});
	}
}
