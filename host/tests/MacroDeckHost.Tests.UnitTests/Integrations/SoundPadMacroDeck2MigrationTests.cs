using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.SoundPad;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
public class SoundPadMacroDeck2MigrationTests
{
	private static Task<ActionMigrationResult?> Migrate(string actionType, string? configuration)
		=> new SoundPadMacroDeck2Migration().MigrateActionAsync(
			new ForeignAction($"PW.MacroDeck.SoundPad.Actions.{actionType}",
				"SoundPadPlugin",
				"SoundPad button",
				configuration,
				null),
			CancellationToken.None);

	[Test]
	public void ClaimsTheMacroDeck2PluginAssembly()
		=> Assert.That(new SoundPadMacroDeck2Migration().ClaimedActionSources, Is.EqualTo(new[] { "SoundPadPlugin" }));

	[TestCase("""{"AudioIndex":-1,"Sound":{"Index":7,"Title":"Airhorn"},"Category":{"Index":2,"Name":"Memes"}}""",
		"7")]
	[TestCase("""{"AudioIndex":4}""", "4")]
	public async Task PlayAction_BecomesPlaySoundForTheSameSound(string configuration, string expectedSound)
	{
		var result = await Migrate("PlayAction", configuration);

		Assert.Multiple(() =>
		{
			Assert.That(result?.IntegrationId, Is.EqualTo(SoundPadIntegration.IntegrationId));
			Assert.That(result?.ActionId, Is.EqualTo("play-sound"));
			Assert.That(result?.Parameters["track"].GetString(), Is.EqualTo(expectedSound));
		});
	}

	[TestCase("""{"AudioIndex":-1}""")]
	[TestCase("")]
	[TestCase("not json")]
	public async Task PlayAction_WithoutASound_IsNotMigrated(string configuration)
		=> Assert.That(await Migrate("PlayAction", configuration), Is.Null);

	[TestCase("""{"RecordingDevice":0}""", "microphone")]
	[TestCase("""{"RecordingDevice":1}""", "speakers")]
	[TestCase("""{"RecordingDevice":"Speakers"}""", "speakers")]
	[TestCase("", "microphone")]
	public async Task StartRecordingAction_KeepsTheRecordingDevice(string configuration, string expectedSource)
	{
		var result = await Migrate("StartRecordingAction", configuration);

		Assert.Multiple(() =>
		{
			Assert.That(result?.ActionId, Is.EqualTo("start-recording"));
			Assert.That(result?.Parameters["source"].GetString(), Is.EqualTo(expectedSource));
		});
	}

	[TestCase("StopPlaybackAction", "stop-playback")]
	[TestCase("StopRecordingAction", "stop-recording")]
	public async Task StopActions_MapOneToOne(string actionType, string expectedAction)
	{
		var result = await Migrate(actionType, null);

		Assert.Multiple(() =>
		{
			Assert.That(result?.ActionId, Is.EqualTo(expectedAction));
			Assert.That(result?.Label, Is.EqualTo("SoundPad button"));
			Assert.That(result?.Warnings, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task UnknownAction_IsNotMigrated()
		=> Assert.That(await Migrate("SomethingElseAction", null), Is.Null);
}
