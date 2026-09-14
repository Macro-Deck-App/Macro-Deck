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
}
