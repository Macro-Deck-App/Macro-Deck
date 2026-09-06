using System.Text.Json;
using MacroDeck.Sdk.Migration;

namespace MacroDeckHost.Integrations.YtmDesktop;

/// <summary>
/// Translates Macro Deck 2's WebNowPlaying plugin into <see cref="YtmDesktopIntegration" />'s
/// music-player actions. Kept out of <see cref="YtmDesktopIntegration" /> itself so that class stays
/// readable; this is only ever called through its <see cref="IIntegrationMigration" /> members.
/// </summary>
internal sealed class YtmDesktopMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	// WebNowPlaying Plugin.csproj's AssemblyName, also the "dll" field of its ExtensionManifest.json
	// minus the extension.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["WebNowPlaying Plugin"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author ("jbcarreon123")
	// and display name ("WebNowPlaying Plugin"), lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["jbcarreon123_webnowplaying plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"jbcarreon123.WebNowPlayingPlugin.Actions.PlayPauseAction" => Simple(action,
			"toggle-play-pause",
			"Play/Pause"),
		"jbcarreon123.WebNowPlayingPlugin.Actions.NextAction" => Simple(action, "next", "Next"),
		"jbcarreon123.WebNowPlayingPlugin.Actions.PreviousAction" => Simple(action, "previous", "Previous"),
		"jbcarreon123.WebNowPlayingPlugin.Actions.ShuffleAction" => MigrateShuffle(action),
		// WebNowPlaying's "Repeat" cycles to whatever the site's next repeat state is (the set of states
		// varies per site), while Macro Deck 3's "Set Repeat Mode" always sets one specific state - there
		// is no fixed target that reproduces a cycle.
		"jbcarreon123.WebNowPlayingPlugin.Actions.RepeatAction" => null,
		_ => null
	};

	// WebNowPlaying never had a plugin settings or credentials screen - the source browser tab is picked
	// per Macro Deck 2 install through its own extension pairing, nothing this migrator can read.
	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings) => [];

	private static ActionMigrationResult MigrateShuffle(ForeignAction action)
		=> new(YtmDesktopIntegration.IntegrationId,
			"toggle-shuffle",
			action.DisplayName ?? "Toggle shuffle",
			Parameters(("mode", JsonSerializer.SerializeToElement("toggle"))));

	private static ActionMigrationResult Simple(ForeignAction action, string actionId, string label)
		=> new(YtmDesktopIntegration.IntegrationId, actionId, action.DisplayName ?? label, Parameters());

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
