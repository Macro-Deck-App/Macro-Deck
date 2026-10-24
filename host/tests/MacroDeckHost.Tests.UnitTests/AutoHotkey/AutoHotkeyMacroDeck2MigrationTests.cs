using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.AutoHotkey;
using MacroDeckHost.Integrations.AutoHotkey.Migration;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class AutoHotkeyMacroDeck2MigrationTests
{
	private static Task<ActionMigrationResult?> Migrate(string actionType, string? configuration)
	{
		return new AutoHotkeyMacroDeck2Migration().MigrateActionAsync(new ForeignAction(
				$"jbcarreon123.AHKPlugin.Actions.{actionType}",
				"AutoHotkey Plugin",
				"AHK button",
				configuration,
				null),
			CancellationToken.None);
	}

	[Test]
	public void Claims_the_community_plugin_assembly()
	{
		Assert.That(new AutoHotkeyMacroDeck2Migration().ClaimedActionSources,
			Is.EqualTo(new[] { "AutoHotkey Plugin" }));
	}

	[Test]
	public async Task A_version_2_file_action_becomes_run_file()
	{
		var result = await Migrate("RunScriptv2Action", """{"ahkScriptPath":"C:\\Scripts\\mute.ahk"}""");

		Assert.Multiple(() =>
		{
			Assert.That(result?.IntegrationId, Is.EqualTo(AutoHotkeyIntegration.IntegrationId));
			Assert.That(result?.ActionId, Is.EqualTo("run-file"));
			Assert.That(result?.Parameters["file"].GetString(), Is.EqualTo(@"C:\Scripts\mute.ahk"));
			Assert.That(result?.Parameters["whenRunning"].GetString(), Is.EqualTo("start-another"));
		});
	}

	[Test]
	public async Task Version_2_inline_code_becomes_inline_code_and_warns_about_old_placeholders()
	{
		var plain = await Migrate("WriteScriptv2Action", """{"script":"MsgBox \"hi\""}""");
		var templated = await Migrate("WriteScriptv2Action", """{"script":"MsgBox \"{{ name }}\""}""");

		Assert.Multiple(() =>
		{
			Assert.That(plain?.ActionId, Is.EqualTo("run-inline-code"));
			Assert.That(plain?.Parameters["code"].GetString(), Is.EqualTo("MsgBox \"hi\""));
			Assert.That(templated?.Warnings?.Count, Is.EqualTo((plain?.Warnings?.Count ?? 0) + 1));
		});
	}

	[Test]
	public async Task Window_spy_migrates_without_parameters()
	{
		var result = await Migrate("WindowSpyAction", null);

		Assert.Multiple(() =>
		{
			Assert.That(result?.ActionId, Is.EqualTo("open-window-spy"));
			Assert.That(result?.Parameters, Is.Empty);
		});
	}

	[TestCase("RunScriptAction", """{"ahkScriptPath":"C:\\v1.ahk"}""")]
	[TestCase("WriteScriptAction", """{"script":"MsgBox, hi"}""")]
	[TestCase("RunScriptv2Action", "not json")]
	public async Task Version_1_actions_and_unreadable_configurations_are_not_migrated(
		string actionType,
		string configuration)
	{
		Assert.That(await Migrate(actionType, configuration), Is.Null);
	}
}
