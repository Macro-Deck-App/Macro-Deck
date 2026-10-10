using MacroDeck.Sdk.Input;

namespace MacroDeck.Sdk.Tests.UnitTests.Input;

[TestFixture]
public class KeyNamesTests
{
	[TestCase("A", KeyCode.A)]
	[TestCase("z", KeyCode.Z)]
	[TestCase("5", KeyCode.D5)]
	[TestCase("D5", KeyCode.D5)]
	[TestCase("Minus", KeyCode.Minus)]
	[TestCase("-", KeyCode.Minus)]
	[TestCase("=", KeyCode.Equal)]
	[TestCase("[", KeyCode.BracketLeft)]
	[TestCase("]", KeyCode.BracketRight)]
	[TestCase("\\", KeyCode.Backslash)]
	[TestCase(";", KeyCode.Semicolon)]
	[TestCase("'", KeyCode.Quote)]
	[TestCase(",", KeyCode.Comma)]
	[TestCase(".", KeyCode.Period)]
	[TestCase("/", KeyCode.Slash)]
	[TestCase("`", KeyCode.Backquote)]
	[TestCase("IntlBackslash", KeyCode.IntlBackslash)]
	[TestCase("Esc", KeyCode.Escape)]
	[TestCase("Return", KeyCode.Enter)]
	[TestCase("Del", KeyCode.Delete)]
	[TestCase("Ins", KeyCode.Insert)]
	[TestCase("PgUp", KeyCode.PageUp)]
	[TestCase("PgDn", KeyCode.PageDown)]
	[TestCase("Up", KeyCode.ArrowUp)]
	[TestCase("Left", KeyCode.ArrowLeft)]
	[TestCase("NumpadEnter", KeyCode.NumpadEnter)]
	[TestCase("NumLock", KeyCode.NumLock)]
	[TestCase("MediaPlayPause", KeyCode.MediaPlayPause)]
	[TestCase("VolumeUp", KeyCode.AudioVolumeUp)]
	[TestCase(" Return ", KeyCode.Enter)]
	public void Every_spelling_the_host_accepts_resolves_to_one_key(string name, KeyCode expected)
	{
		Assert.That(KeyNames.TryParse(name, out var key), Is.True);
		Assert.That(key, Is.EqualTo(expected));
	}

	[TestCase("_")]
	[TestCase("SS")]
	[TestCase("Ü")]
	[TestCase("<")]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase(null)]
	public void A_name_that_means_no_key_does_not_resolve(string? name)
	{
		Assert.That(KeyNames.TryParse(name, out var key), Is.False);
		Assert.That(key, Is.EqualTo(KeyCode.None));
	}

	[TestCase("Ctrl", KeyModifier.Control)]
	[TestCase("control", KeyModifier.Control)]
	[TestCase("Shift", KeyModifier.Shift)]
	[TestCase("Alt", KeyModifier.Alt)]
	[TestCase("Option", KeyModifier.Alt)]
	[TestCase("AltGr", KeyModifier.Alt)]
	[TestCase("Meta", KeyModifier.Meta)]
	[TestCase("Cmd", KeyModifier.Meta)]
	[TestCase("Command", KeyModifier.Meta)]
	[TestCase("Win", KeyModifier.Meta)]
	[TestCase("Super", KeyModifier.Meta)]
	[TestCase("RightCtrl", KeyModifier.RightControl)]
	[TestCase("RightShift", KeyModifier.RightShift)]
	[TestCase("RightAlt", KeyModifier.RightAlt)]
	[TestCase("RightMeta", KeyModifier.RightMeta)]
	public void Every_modifier_spelling_the_host_accepts_resolves_to_one_modifier(string name, KeyModifier expected)
	{
		Assert.That(KeyNames.TryParseModifier(name, out var modifier), Is.True);
		Assert.That(modifier, Is.EqualTo(expected));
	}

	[TestCase("Hyper")]
	[TestCase("")]
	[TestCase(null)]
	public void A_name_that_means_no_modifier_does_not_resolve(string? name)
		=> Assert.That(KeyNames.TryParseModifier(name, out _), Is.False);

	[TestCase(KeyCode.A, "A")]
	[TestCase(KeyCode.Z, "Z")]
	[TestCase(KeyCode.D0, "0")]
	[TestCase(KeyCode.D9, "9")]
	[TestCase(KeyCode.F1, "F1")]
	[TestCase(KeyCode.F24, "F24")]
	[TestCase(KeyCode.Minus, "Minus")]
	[TestCase(KeyCode.Backquote, "Backquote")]
	[TestCase(KeyCode.IntlBackslash, "IntlBackslash")]
	[TestCase(KeyCode.Enter, "Enter")]
	[TestCase(KeyCode.ArrowUp, "ArrowUp")]
	[TestCase(KeyCode.NumLock, "NumLock")]
	[TestCase(KeyCode.Numpad0, "Numpad0")]
	[TestCase(KeyCode.NumpadEnter, "NumpadEnter")]
	[TestCase(KeyCode.MediaTrackNext, "MediaTrackNext")]
	[TestCase(KeyCode.AudioVolumeMute, "AudioVolumeMute")]
	[TestCase(KeyCode.None, "")]
	public void The_name_of_a_key_is_the_one_the_combo_editor_stores(KeyCode key, string expected)
		=> Assert.That(KeyNames.ToName(key), Is.EqualTo(expected));

	[Test]
	public void Every_key_resolves_again_from_its_own_name()
	{
		Assert.Multiple(() =>
		{
			foreach (var key in Enum.GetValues<KeyCode>().Where(key => key != KeyCode.None))
			{
				Assert.That(KeyNames.TryParse(KeyNames.ToName(key), out var parsed), Is.True, key.ToString());
				Assert.That(parsed, Is.EqualTo(key), key.ToString());
			}
		});
	}

	[TestCase(KeyCode.LeftControl, KeyModifier.Control)]
	[TestCase(KeyCode.RightControl, KeyModifier.RightControl)]
	[TestCase(KeyCode.LeftShift, KeyModifier.Shift)]
	[TestCase(KeyCode.RightShift, KeyModifier.RightShift)]
	[TestCase(KeyCode.LeftAlt, KeyModifier.Alt)]
	[TestCase(KeyCode.RightAlt, KeyModifier.RightAlt)]
	[TestCase(KeyCode.LeftMeta, KeyModifier.Meta)]
	[TestCase(KeyCode.RightMeta, KeyModifier.RightMeta)]
	public void A_modifier_key_stands_for_its_modifier(KeyCode key, KeyModifier expected)
	{
		Assert.That(KeyNames.TryGetModifier(key, out var modifier), Is.True);
		Assert.That(modifier, Is.EqualTo(expected));
	}

	[TestCase(KeyCode.A)]
	[TestCase(KeyCode.CapsLock)]
	[TestCase(KeyCode.None)]
	public void Another_key_stands_for_no_modifier(KeyCode key)
		=> Assert.That(KeyNames.TryGetModifier(key, out _), Is.False);

	[Test]
	public void Held_modifiers_are_named_in_the_order_the_editor_stores_them()
		=> Assert.That(Names(KeyModifier.Meta | KeyModifier.Alt | KeyModifier.Shift | KeyModifier.Control), Is.EqualTo("Ctrl,Shift,Alt,Meta"));

	[Test]
	public void A_modifier_held_only_on_the_right_is_named_with_its_side()
		=> Assert.That(Names(KeyModifier.RightControl | KeyModifier.RightShift), Is.EqualTo("RightCtrl,RightShift"));

	[Test]
	public void A_modifier_held_on_both_sides_is_named_like_the_editor_names_it()
		=> Assert.That(Names(KeyModifier.Control | KeyModifier.RightControl), Is.EqualTo("Ctrl"));

	[Test]
	public void No_held_modifier_has_no_names()
		=> Assert.That(KeyNames.ToModifierNames(KeyModifier.None), Is.Empty);

	private static string Names(KeyModifier modifiers) => string.Join(',', KeyNames.ToModifierNames(modifiers));

	[Test]
	public void The_names_of_held_modifiers_resolve_again_to_the_same_modifiers()
	{
		var held = KeyModifier.Control | KeyModifier.RightShift | KeyModifier.Meta;

		var resolved = KeyNames.ToModifierNames(held)
			.Aggregate(KeyModifier.None, (all, name) => KeyNames.TryParseModifier(name, out var modifier) ? all | modifier : all);

		Assert.That(resolved, Is.EqualTo(held));
	}
}
