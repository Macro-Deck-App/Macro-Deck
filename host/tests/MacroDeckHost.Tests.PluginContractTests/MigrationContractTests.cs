using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Migration;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The <c>migration</c> capability from the host's side of a connection: an out-of-process plugin
/// contributes migrations exactly as a built-in integration does, so a setup that mentions its actions
/// survives the move from another application.
/// </summary>
[TestFixture]
internal sealed class MigrationContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Migration,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private Task<MacroDeck.Sdk.IIntegration> ConnectMigrationAsync(params IIntegrationMigration[] migrations)
		=> ConnectAsync([new MigrationCapabilityHandler([new TestMigrationIntegration(migrations)])],
			[Provider()],
			[CapabilityKinds.Migration]);

	[Test]
	public async Task Declare_tells_the_host_which_source_the_plugin_migrates_from_and_what_it_claims()
	{
		var integration = await ConnectMigrationAsync(new TestIntegrationMigration
		{
			Source = MigrationSource.MacroDeck2,
			ClaimedActionSources = ["OBS-WebSocket Plugin"],
			ClaimedSettingsSources = ["macro deck_obs-websocket plugin"]
		});

		var migration = ((IMigrationProvider)integration).Migrations.Single();

		Assert.Multiple(() =>
		{
			Assert.That(migration.Source, Is.EqualTo(MigrationSource.MacroDeck2));
			Assert.That(migration.ClaimedActionSources, Is.EqualTo(new[] { "OBS-WebSocket Plugin" }));
			Assert.That(migration.ClaimedSettingsSources, Is.EqualTo(new[] { "macro deck_obs-websocket plugin" }));
		});
	}

	[Test]
	public async Task A_translated_action_arrives_with_the_parameters_and_warnings_the_plugin_returned()
	{
		ForeignAction? seen = null;
		var integration = await ConnectMigrationAsync(new TestIntegrationMigration
		{
			ClaimedActionSources = ["OBS-WebSocket Plugin"],
			TranslateAction = action =>
			{
				seen = action;
				return new ActionMigrationResult("com.example.obs",
					"set-scene",
					"Switch to Gaming",
					new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						["scene"] = JsonSerializer.SerializeToElement("Gaming")
					},
					["The connection this button used is not migrated"]);
			}
		});

		var result = await ((IMigrationProvider)integration).Migrations.Single()
			.MigrateActionAsync(new ForeignAction("SuchByte.OBSWebSocketPlugin.Actions.SetSceneAction",
					"OBS-WebSocket Plugin",
					"Set scene",
					"{\"SceneName\":\"Gaming\"}",
					"Gaming"),
				CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(seen?.TypeName, Is.EqualTo("SuchByte.OBSWebSocketPlugin.Actions.SetSceneAction"));
			Assert.That(seen?.Configuration, Is.EqualTo("{\"SceneName\":\"Gaming\"}"));
			Assert.That(result?.IntegrationId, Is.EqualTo("com.example.obs"));
			Assert.That(result?.ActionId, Is.EqualTo("set-scene"));
			Assert.That(result?.Label, Is.EqualTo("Switch to Gaming"));
			Assert.That(result?.Parameters["scene"].GetString(), Is.EqualTo("Gaming"));
			Assert.That(result?.Warnings?.Select(warning => warning.Literal),
				Is.EqualTo(new[] { "The connection this button used is not migrated" }));
		});
	}

	/// <summary>
	/// "No equivalent" has to reach the host as an answer rather than as an error, because that is what
	/// makes the difference between a placeholder carrying the original configuration and a failed import.
	/// </summary>
	[Test]
	public async Task A_plugin_with_no_equivalent_answers_nothing_rather_than_failing()
	{
		var integration = await ConnectMigrationAsync(new TestIntegrationMigration
			{ ClaimedActionSources = ["OBS-WebSocket Plugin"], TranslateAction = _ => null });

		var result = await ((IMigrationProvider)integration).Migrations.Single()
			.MigrateActionAsync(new ForeignAction("Whatever", "OBS-WebSocket Plugin", null, null, null),
				CancellationToken.None);

		Assert.That(result, Is.Null);
	}

	/// <summary>A plugin that throws costs the same placeholder, not a migration that stops halfway.</summary>
	[Test]
	public async Task A_plugin_that_fails_costs_one_placeholder_rather_than_the_migration()
	{
		var integration = await ConnectMigrationAsync(new TestIntegrationMigration
		{
			ClaimedActionSources = ["OBS-WebSocket Plugin"],
			TranslateAction = _ => throw new InvalidOperationException("boom")
		});

		var result = await ((IMigrationProvider)integration).Migrations.Single()
			.MigrateActionAsync(new ForeignAction("Whatever", "OBS-WebSocket Plugin", null, null, null),
				CancellationToken.None);

		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task Migrated_configuration_carries_its_values_and_each_secrets_kind_across()
	{
		ForeignPluginSettings? seen = null;
		var integration = await ConnectMigrationAsync(new TestIntegrationMigration
		{
			ClaimedSettingsSources = ["macro deck_obs-websocket plugin"],
			TranslateConfiguration = settings =>
			{
				seen = settings;
				return
				[
					new MigratedConfiguration("com.example.obs",
						"OBS Connection",
						new Dictionary<string, JsonElement>(StringComparer.Ordinal)
						{
							["host"] = JsonSerializer.SerializeToElement("127.0.0.1"),
							["port"] = JsonSerializer.SerializeToElement(4455)
						},
						new Dictionary<string, MigratedSecret>(StringComparer.Ordinal)
						{
							["password"] = new("hunter2", MigratedSecretKind.Password)
						})
				];
			}
		});

		var configurations = await ((IMigrationProvider)integration).Migrations.Single()
			.MigrateConfigurationAsync(new ForeignPluginSettings("macro deck_obs-websocket plugin",
					new Dictionary<string, string>(StringComparer.Ordinal) { ["autoConnect"] = "true" },
					[new Dictionary<string, string>(StringComparer.Ordinal) { ["host"] = "127.0.0.1:4455" }],
					[new ForeignAction("SetSceneAction", "OBS-WebSocket Plugin", null, "{\"Scene\":\"Intro\"}", null)]),
				CancellationToken.None);

		var configuration = configurations.Single();

		Assert.Multiple(() =>
		{
			Assert.That(seen?.Settings["autoConnect"], Is.EqualTo("true"));
			Assert.That(seen?.Credentials.Single()["host"], Is.EqualTo("127.0.0.1:4455"));

			// The plugin's own actions travel with its settings: a source application need not have kept
			// all of a plugin's configuration in one place.
			Assert.That(seen?.Actions.Single().Configuration, Is.EqualTo("{\"Scene\":\"Intro\"}"));
			Assert.That(configuration.IntegrationId, Is.EqualTo("com.example.obs"));
			Assert.That(configuration.Title, Is.EqualTo("OBS Connection"));
			Assert.That(configuration.Values["host"].GetString(), Is.EqualTo("127.0.0.1"));
			Assert.That(configuration.Values["port"].GetInt32(), Is.EqualTo(4455));
			Assert.That(configuration.Secrets["password"].Value, Is.EqualTo("hunter2"));
			Assert.That(configuration.Secrets["password"].Kind, Is.EqualTo(MigratedSecretKind.Password));
		});
	}

	/// <summary>
	/// The declared list is what the host answers "which migrations does this integration support" with, so
	/// a plugin serving two applications has to arrive as two entries rather than one merged claim.
	/// </summary>
	[Test]
	public async Task A_plugin_that_reads_two_applications_declares_one_migration_for_each()
	{
		var integration = await ConnectMigrationAsync(new TestIntegrationMigration
				{ Source = MigrationSource.MacroDeck2, ClaimedActionSources = ["md2"] },
			new TestIntegrationMigration { Source = MigrationSource.TouchPortal, ClaimedActionSources = ["tp"] });

		var migrations = ((IMigrationProvider)integration).Migrations;

		Assert.Multiple(() =>
		{
			Assert.That(migrations.Select(migration => migration.Source),
				Is.EquivalentTo(new[] { MigrationSource.MacroDeck2, MigrationSource.TouchPortal }));
			Assert.That(migrations.Single(migration => migration.Source == MigrationSource.TouchPortal)
					.ClaimedActionSources,
				Is.EqualTo(new[] { "tp" }));
		});
	}
}
