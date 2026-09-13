using System.Text.Json;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;

namespace MacroDeckHost.Integrations.WebNowPlaying;

internal sealed class WebNowPlayingMacroDeck2Migration : IIntegrationMigration
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
		=> Task.FromResult<IReadOnlyList<MigratedConfiguration>>([]);

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"jbcarreon123.WebNowPlayingPlugin.Actions.PlayPauseAction" => Translated(action,
			"toggle-play-pause",
			"Play/Pause"),
		"jbcarreon123.WebNowPlayingPlugin.Actions.NextAction" => Translated(action, "next", "Next"),
		"jbcarreon123.WebNowPlayingPlugin.Actions.PreviousAction" => Translated(action, "previous", "Previous"),
		"jbcarreon123.WebNowPlayingPlugin.Actions.ShuffleAction" => Translated(action,
			"toggle-shuffle",
			"Toggle shuffle",
			("mode", JsonSerializer.SerializeToElement("toggle"))),
		// WebNowPlaying's "Repeat" cycles to whatever the site's next repeat state is (the set of states
		// varies per site), while Macro Deck 3's "Set Repeat Mode" always sets one specific state.
		_ => null
	};

	private static ActionMigrationResult Translated(
		ForeignAction action,
		string actionId,
		string label,
		params (string Name, JsonElement Value)[] parameters)
		=> new(WebNowPlayingIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? label,
			parameters.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal),
			[AppStrings.Integrations.WebNowPlaying.Migration.EnableIntegration()]);
}
