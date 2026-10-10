using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.System;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
public class WindowsUtilsMacroDeck2MigrationTests
{
	private static async Task<ActionMigrationResult> MigrateHotkey(string configuration)
	{
		var result = await new WindowsUtilsMacroDeck2Migration().MigrateActionAsync(
			new ForeignAction("SuchByte.WindowsUtils.Actions.HotkeyAction", "Windows Utils", null, configuration, null),
			CancellationToken.None);
		return result!;
	}

	private static string[] Modifiers(ActionMigrationResult result)
		=> result.Parameters["combo"].GetProperty("modifiers").EnumerateArray().Select(m => m.GetString()!).ToArray();

	[Test]
	public async Task A_hotkey_that_required_the_right_hand_modifier_keeps_requiring_it()
	{
		var result = await MigrateHotkey("""{"key":"F1","ralt":true,"rctrl":true}""");

		Assert.Multiple(() =>
		{
			Assert.That(Modifiers(result), Is.EquivalentTo(new[] { "RightCtrl", "RightAlt" }));
			Assert.That(result.Warnings, Is.Null.Or.Empty);
		});
	}

	[Test]
	public async Task A_hotkey_with_the_generic_or_left_hand_modifier_presses_the_left_key_as_before()
	{
		var result = await MigrateHotkey("""{"key":"F1","alt":true,"lctrl":true,"rctrl":true}""");

		Assert.That(Modifiers(result), Is.EquivalentTo(new[] { "ctrl", "alt" }));
	}

	private static async Task<string> MigratedKey(string virtualKey, Func<ushort, ushort>? virtualKeyToScanCode)
	{
		var result = await new WindowsUtilsMacroDeck2Migration(virtualKeyToScanCode).MigrateActionAsync(
			new ForeignAction("SuchByte.WindowsUtils.Actions.HotkeyAction", "Windows Utils", null,
				$$"""{"key":"{{virtualKey}}"}""", null),
			CancellationToken.None);
		return result!.Parameters["combo"].GetProperty("key").GetString()!;
	}

	private static ushort GermanLayout(ushort virtualKey) => virtualKey switch
	{
		0xBD => 0x35,
		0xDB => 0x0C,
		0xE2 => 0x56,
		_ => 0
	};

	[TestCase("OEM_MINUS", "Slash")]
	[TestCase("OEM_4", "Minus")]
	[TestCase("OEM_102", "IntlBackslash")]
	public async Task A_punctuation_hotkey_keeps_the_key_it_pressed_on_the_migrating_layout(string virtualKey, string expected)
		=> Assert.That(await MigratedKey(virtualKey, GermanLayout), Is.EqualTo(expected));

	[Test]
	public async Task A_punctuation_hotkey_on_a_letter_position_does_not_become_that_letter()
		=> Assert.That(await MigratedKey("OEM_COMMA", virtualKey => virtualKey == 0xBC ? (ushort)0x32 : (ushort)0),
			Is.EqualTo("Comma"));

	[TestCase("OEM_MINUS", "Minus")]
	[TestCase("OEM_102", "IntlBackslash")]
	public async Task Without_a_layout_punctuation_hotkeys_use_the_us_positions(string virtualKey, string expected)
		=> Assert.That(await MigratedKey(virtualKey, null), Is.EqualTo(expected));
}
