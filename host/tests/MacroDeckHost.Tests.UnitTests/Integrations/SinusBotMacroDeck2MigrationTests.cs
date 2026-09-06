using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.SinusBot;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

/// <summary>
/// Macro Deck 2 kept the SinusBot server login in its settings file and the bot instance to play on in
/// each button, so a migration that reads only the settings file produces an entry that reads as
/// configured and can never build a player.
/// </summary>
[TestFixture]
public class SinusBotMacroDeck2MigrationTests
{
	private static readonly IReadOnlyDictionary<string, string> _login = new Dictionary<string, string>
	{
		["url"] = "https://sinusbot.example",
		["username"] = "someone@example.com",
		["password"] = "hunter2"
	};

	private static ForeignAction PlayBackFile(string? instanceId)
		=> new("SuchByte.SinusBotPlugin.Actions.PlayBackFileAction",
			"SinusBot Plugin",
			"Playback file",
			instanceId is null
				? """{"FileId":"file-1"}"""
				: $$"""{"InstanceId":"{{instanceId}}","FileId":"file-1"}""",
			null);

	private static Task<IReadOnlyList<MigratedConfiguration>> Migrate(params ForeignAction[] actions)
		=> new SinusBotMacroDeck2Migration().MigrateConfigurationAsync(new ForeignPluginSettings(
				"macro deck_sinusbot plugin",
				new Dictionary<string, string>(StringComparer.Ordinal),
				[_login],
				actions),
			CancellationToken.None);

	[Test]
	public async Task MigrateConfiguration_TakesTheBotInstanceFromTheButtonsThatUsedIt()
	{
		var configurations = await Migrate(PlayBackFile("instance-a"));

		var configuration = configurations.Single();

		Assert.Multiple(() =>
		{
			Assert.That(configuration.IntegrationId, Is.EqualTo(SinusBotIntegration.IntegrationId));
			Assert.That(configuration.Values["serverUrl"].GetString(), Is.EqualTo("https://sinusbot.example"));
			Assert.That(configuration.Values["username"].GetString(), Is.EqualTo("someone@example.com"));
			Assert.That(configuration.Values["instanceId"].GetString(), Is.EqualTo("instance-a"));
			Assert.That(configuration.Secrets["password"].Value, Is.EqualTo("hunter2"));
		});
	}

	/// <summary>One entry holds one instance, so the one most buttons played on is the one that survives.</summary>
	[Test]
	public async Task MigrateConfiguration_WithSeveralInstances_TakesTheOneMostButtonsUsed()
	{
		var configurations = await Migrate(PlayBackFile("rarely-used"),
			PlayBackFile("everyday"),
			PlayBackFile("everyday"));

		Assert.That(configurations.Single().Values["instanceId"].GetString(), Is.EqualTo("everyday"));
	}

	/// <summary>
	/// Better no entry than one that shows SinusBot as configured while no player can ever be built from
	/// it - the user then sets it up through the normal config flow and knows they have to.
	/// </summary>
	[Test]
	public async Task MigrateConfiguration_WithoutAnInstanceAnywhere_MigratesNothing()
	{
		var configurations = await Migrate(PlayBackFile(instanceId: null));

		Assert.That(configurations, Is.Empty);
	}
}
