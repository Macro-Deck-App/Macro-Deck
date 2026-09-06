using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Spotify;

/// <summary>
/// Translates Macro Deck 2's Spotify plugin into <see cref="SpotifyIntegration" />'s music-player
/// actions and configuration entries. Kept out of <see cref="SpotifyIntegration" /> itself so that class
/// stays readable; this is only ever called through its <see cref="IIntegrationMigration" /> members.
/// </summary>
internal sealed class SpotifyMacroDeck2Migration : IIntegrationMigration
{
	private const int VolumeStepPercent = 10;

	public MigrationSource Source => MigrationSource.MacroDeck2;

	// Develeon64.SpotifyPlugin.csproj's AssemblyName, also the "dll" field of its ExtensionManifest.json
	// minus the extension.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["SpotifyPlugin"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author and display name,
	// lowercased and joined with an underscore. The plugin's author changed from "Develeon64" to
	// "SuchByte, Develeon64" between versions, so both file names can exist on disk.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } =
		["develeon64_spotifyplugin", "suchbyte, develeon64_spotifyplugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"Develeon64.SpotifyPlugin.Actions.LibraryActionAction" => MigrateLibrary(action),
		"Develeon64.SpotifyPlugin.Actions.LoopAction" => MigrateLoop(action),
		"Develeon64.SpotifyPlugin.Actions.PauseAction" => MigratePause(action),
		"Develeon64.SpotifyPlugin.Actions.PlaylistAction" => MigratePlaylist(action),
		"Develeon64.SpotifyPlugin.Actions.PreviousAction" => Simple(action, "previous", "Previous track"),
		"Develeon64.SpotifyPlugin.Actions.ShuffleAction" => MigrateShuffle(action),
		"Develeon64.SpotifyPlugin.Actions.SkipAction" => Simple(action, "next", "Next track"),
		"Develeon64.SpotifyPlugin.Actions.VolumeAction" => MigrateVolume(action),
		_ => null
	};

	// The plugin never collects a Spotify app client secret (it authorizes with PKCE), but
	// SpotifyConfigFlow's authorization-code exchange - and every later token refresh - requires one.
	// Carrying over the access/refresh token pair without it would produce a connection that reads as
	// configured and then fails the moment the short-lived access token expires, so nothing is migrated;
	// the user reconnects Spotify through the normal config flow instead.
	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings) => [];

	private static ActionMigrationResult MigrateLibrary(ForeignAction action)
	{
		var mode = ReadModeIndex(action.Configuration) switch
		{
			1 => "add",
			2 => "remove",
			_ => "toggle"
		};

		return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
			"toggle-liked",
			action.DisplayName ?? "Save/remove track",
			Parameters(("mode", JsonString(mode))));
	}

	private static ActionMigrationResult MigrateLoop(ForeignAction action)
	{
		// LoopAction's "Toggle" mode does not flip the current repeat state - it always targets track
		// repeat, same as SetRepeatModeAsync("track") below - so a direct value mapping is exact, not
		// approximate.
		var mode = ReadModeIndex(action.Configuration) switch
		{
			1 => "context",
			2 => "off",
			_ => "track"
		};

		return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
			"set-repeat-mode",
			action.DisplayName ?? "Set repeat mode",
			Parameters(("mode", JsonString(mode))));
	}

	private static ActionMigrationResult MigratePause(ForeignAction action)
	{
		var actionId = ReadModeIndex(action.Configuration) switch
		{
			1 => "play",
			2 => "pause",
			_ => "toggle-play-pause"
		};

		return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? "Play/pause",
			Parameters());
	}

	private static ActionMigrationResult? MigratePlaylist(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var uri = ReadString(config, "Uri");
		if (string.IsNullOrWhiteSpace(uri))
		{
			return null;
		}

		// "Library" played the user's saved tracks, a pseudo-playlist Macro Deck 3's catalog has no
		// entry for; there is no playlist id to substitute without changing what the button plays.
		if (string.Equals(uri, "Library", StringComparison.Ordinal))
		{
			return null;
		}

		var warnings = new List<LocalizedText>();
		if (ReadInt(config, "Track") is { } track and not 0)
		{
			warnings.Add(AppStrings.Migration.Warning.Spotify.PlaylistStartPositionIgnored(position: track));
		}

		return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
			"play-playlist",
			action.DisplayName ?? "Play playlist",
			Parameters(("playlist", JsonString(uri))),
			warnings.Count > 0 ? warnings : null);
	}

	private static ActionMigrationResult MigrateShuffle(ForeignAction action)
	{
		var mode = ReadModeIndex(action.Configuration) switch
		{
			1 => "on",
			2 => "off",
			_ => "toggle"
		};

		return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
			"toggle-shuffle",
			action.DisplayName ?? "Toggle shuffle",
			Parameters(("mode", JsonString(mode))));
	}

	private static ActionMigrationResult MigrateVolume(ForeignAction action)
	{
		var value = TryParseConfig(action.Configuration, out var config) ? ReadInt(config, "Value") ?? 100 : 100;

		switch (ReadModeIndex(action.Configuration))
		{
			case 1:
				return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
					"volume-up",
					action.DisplayName ?? "Increase volume",
					Parameters(),
					value == 10 ? null : [VolumeStepWarning(value)]);
			case 2:
				return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
					"volume-down",
					action.DisplayName ?? "Decrease volume",
					Parameters(),
					value == 10 ? null : [VolumeStepWarning(value)]);
			default:
				return new ActionMigrationResult(SpotifyIntegration.IntegrationId,
					"set-volume",
					action.DisplayName ?? "Set volume",
					Parameters(("volume", JsonSerializer.SerializeToElement(Math.Clamp(value, 0, 100)))));
		}
	}

	private static LocalizedText VolumeStepWarning(int configuredValue)
		=> AppStrings.Migration.Warning.VolumeStepFixed(value: configuredValue, step: VolumeStepPercent);

	private static ActionMigrationResult Simple(ForeignAction action, string actionId, string label)
		=> new(SpotifyIntegration.IntegrationId, actionId, action.DisplayName ?? label, Parameters());

	private static int? ReadModeIndex(string? configuration)
		=> TryParseConfig(configuration, out var config) ? ReadEnumIndex(config, "Mode") : null;

	private static bool TryParseConfig(string? configuration, out JsonElement config)
	{
		var text = string.IsNullOrWhiteSpace(configuration) ? "{}" : configuration;
		try
		{
			config = JsonDocument.Parse(text).RootElement.Clone();
			return config.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			config = default;
			return false;
		}
	}

	private static bool TryGetProperty(JsonElement config, string name, out JsonElement value)
	{
		foreach (var property in config.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static string? ReadString(JsonElement config, string name)
		=> TryGetProperty(config, name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static int? ReadInt(JsonElement config, string name)
	{
		if (!TryGetProperty(config, name, out var value))
		{
			return null;
		}

		return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
	}

	// System.Text.Json serializes an unattributed enum as its underlying integer, but this plugin's own
	// EMode names (Toggle/Activate/Deactivate) are accepted too since nothing proves a future version
	// couldn't switch to a string converter.
	private static int? ReadEnumIndex(JsonElement config, string name)
	{
		if (!TryGetProperty(config, name, out var value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index))
		{
			return index;
		}

		if (value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() switch
			{
				"Activate" => 1,
				"Deactivate" => 2,
				_ => 0
			};
		}

		return null;
	}

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
