using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.SinusBot;

/// <summary>
/// Translates Macro Deck 2's SinusBot plugin into <see cref="SinusBotIntegration" />'s music-player
/// actions and configuration entries. Kept out of <see cref="SinusBotIntegration" /> itself so that class
/// stays readable; this is only ever called through its <see cref="IIntegrationMigration" /> members.
/// </summary>
internal sealed class SinusBotMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	// "SinusBot Plugin.csproj"'s file name (the project has no explicit AssemblyName, so MSBuild derives
	// it from the project file), also the "dll" field of its ExtensionManifest.json minus the extension.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["SinusBot Plugin"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author ("Macro Deck") and
	// display name ("SinusBot Plugin"), lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["macro deck_sinusbot plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"SuchByte.SinusBotPlugin.Actions.IncreaseVolumeAction" => VolumeStep(action, "volume-up", 5),
		"SuchByte.SinusBotPlugin.Actions.DecreaseVolumeAction" => VolumeStep(action, "volume-down", 5),
		// SinusBot's real pause endpoint is a separate action from Macro Deck 3's music-player set - the
		// bot only exposes a hard stop here, and Macro Deck 3's action set has no "stop" command that
		// would not also change what the button does (its "pause" calls SinusBot's own pause endpoint).
		"SuchByte.SinusBotPlugin.Actions.StopPlayBackAction" => null,
		"SuchByte.SinusBotPlugin.Actions.PlayBackFileAction" => MigratePlayBackFile(action),
		_ => null
	};

	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings)
	{
		var url = ReadCredential(settings.Credentials, "url");
		var username = ReadCredential(settings.Credentials, "username");
		var password = ReadCredential(settings.Credentials, "password");
		if (string.IsNullOrWhiteSpace(url) ||
			string.IsNullOrWhiteSpace(username) ||
			string.IsNullOrWhiteSpace(password))
		{
			return [];
		}

		// Macro Deck 2 stores one server login per plugin, shared by every action button, and the bot
		// instance to talk to in each button's own configuration. Macro Deck 3 keys the instance into the
		// same entry as the login, and an entry without one builds no player at all - it would read as
		// configured and do nothing. So the instance comes from the buttons, which is the only place the
		// source ever recorded it; the most-used one wins, since Macro Deck 3's entry holds exactly one.
		var instanceId = MostUsedInstanceId(settings.Actions);
		if (instanceId is null)
		{
			return [];
		}

		var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[SinusBotConfigKeys.ServerUrl] = JsonSerializer.SerializeToElement(url),
			[SinusBotConfigKeys.Username] = JsonSerializer.SerializeToElement(username),
			[SinusBotConfigKeys.InstanceId] = JsonSerializer.SerializeToElement(instanceId)
		};

		var secrets = new Dictionary<string, MigratedSecret>(StringComparer.Ordinal)
		{
			[SinusBotConfigKeys.Password] = new(password, MigratedSecretKind.Password)
		};

		return [new MigratedConfiguration(SinusBotIntegration.IntegrationId, "SinusBot", values, secrets)];
	}

	/// <summary>
	/// The bot instance the migrated buttons actually played on. Ties go to the first one seen, so the
	/// choice is at least stable across runs of the same setup.
	/// </summary>
	private static string? MostUsedInstanceId(IReadOnlyList<ForeignAction> actions)
	{
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		var order = new List<string>();

		foreach (var action in actions)
		{
			if (!TryParseConfig(action.Configuration, out var config))
			{
				continue;
			}

			var id = ReadString(config, "InstanceId");
			if (string.IsNullOrWhiteSpace(id))
			{
				continue;
			}

			if (!counts.ContainsKey(id))
			{
				order.Add(id);
			}

			counts[id] = counts.GetValueOrDefault(id) + 1;
		}

		return order.Count == 0 ? null : order.OrderByDescending(id => counts[id]).First();
	}

	private static ActionMigrationResult VolumeStep(ForeignAction action, string actionId, int configuredStep)
		=> new(SinusBotIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? actionId,
			Parameters(),
			[AppStrings.Migration.Warning.VolumeStepFixed(value: configuredStep, step: FixedVolumeStepPercent)]);

	private const int FixedVolumeStepPercent = 10;

	private static ActionMigrationResult? MigratePlayBackFile(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var fileId = ReadString(config, "fileId");
		if (string.IsNullOrWhiteSpace(fileId))
		{
			return null;
		}

		var warnings = new List<LocalizedText>();
		if (ReadInt(config, "volume") is { } volume and > -1)
		{
			warnings.Add(AppStrings.Migration.Warning.SinusBot.PlaybackVolumeNotMigrated(volume: volume));
		}

		if (ReadBool(config, "syncButtonState") ?? false)
		{
			warnings.Add(AppStrings.Migration.Warning.SinusBot.ButtonStateSyncAutomatic());
		}

		return new ActionMigrationResult(SinusBotIntegration.IntegrationId,
			"play-track",
			action.DisplayName ?? "Play track",
			Parameters(("track", JsonSerializer.SerializeToElement(fileId))),
			warnings.Count > 0 ? warnings : null);
	}

	private static string? ReadCredential(IReadOnlyList<IReadOnlyDictionary<string, string>> credentials,
		string name)
		=> credentials
			.SelectMany(dict => dict)
			.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
			.Value;

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

	private static bool? ReadBool(JsonElement config, string name)
	{
		if (!TryGetProperty(config, name, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
			_ => null
		};
	}

	private static int? ReadInt(JsonElement config, string name)
	{
		if (!TryGetProperty(config, name, out var value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
		{
			return number;
		}

		return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)
			? parsed
			: null;
	}

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
