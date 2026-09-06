using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs;

/// <summary>
/// Takes an OBS setup over from Macro Deck 2's OBS-WebSocket plugin. One of
/// <see cref="ObsIntegration" />'s declared migrations; the integration itself only lists it.
/// </summary>
internal sealed class ObsMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	private const int ToggleIndex = 2;

	private static readonly string[] VisibilityMethodNames = ["Hide", "Show", "Toggle"];
	private static readonly string[] AudioMethodNames = ["Mute", "Unmute", "Toggle"];
	private static readonly string[] StateMethodNames = ["Start", "Stop", "Toggle"];

	// The plugin's assembly-qualified type name carries this exact string (Macro-Deck-OBS-WebSocket.csproj's
	// AssemblyName, also the "dll" field of its ExtensionManifest.json minus the extension).
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["OBS-WebSocket Plugin"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author ("Macro Deck") and
	// display name ("OBS-WebSocket Plugin"), lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["macro deck_obs-websocket plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings config,
		CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<MigratedConfiguration>>(MigrateConfiguration(config));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"SuchByte.OBSWebSocketPlugin.Actions.SetSceneAction" => MigrateSetScene(action),
		"SuchByte.OBSWebSocketPlugin.Actions.SourceVisibilityAction" => MigrateSourceVisibility(action),
		"SuchByte.OBSWebSocketPlugin.Actions.SetAudioMutedAction" => MigrateSetAudioMuted(action),
		"SuchByte.OBSWebSocketPlugin.Actions.SetFilterStateAction" => MigrateSetFilterState(action),
		"SuchByte.OBSWebSocketPlugin.Actions.SaveReplayBufferAction" => MigrateSaveReplayBuffer(action),
		"SuchByte.OBSWebSocketPlugin.Actions.SetReplayBufferState" => MigrateGenericState(action, "replay-buffer"),
		"SuchByte.OBSWebSocketPlugin.Actions.SetRecordingStateAction" => MigrateGenericState(action, "recording"),
		"SuchByte.OBSWebSocketPlugin.Actions.SetStreamingStateAction" => MigrateGenericState(action, "streaming"),
		"SuchByte.OBSWebSocketPlugin.Actions.SetVirtualCamAction" => MigrateGenericState(action, "virtual-camera"),
		_ => null
	};

	private static List<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings config)
	{
		if (config.Credentials.Count == 0)
		{
			return [];
		}

		var results = new List<MigratedConfiguration>();
		var occupiedKeys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var credentials in config.Credentials)
		{
			var rawHost = ReadCredential(credentials, "host");
			if (string.IsNullOrWhiteSpace(rawHost) || !TryParseHost(rawHost, out var host, out var port))
			{
				continue;
			}

			var title = ReadCredential(credentials, "name") is { Length: > 0 } name ? name : "OBS Connection";

			// ObsIntegration.LoadDesiredAsync only picks up a config entry that carries the schema and
			// identity keys the config-flow UI normally adds through ObsConfigurationMutationAdapter.Prepare.
			// MigrationService writes entries straight to the config store, bypassing that adapter, so this
			// migrator has to allocate and stamp both keys itself or the migrated connection would silently
			// never load.
			var identity = ObsConfigurationIdentityAllocator.Allocate(title, occupiedKeys);
			if (identity is null)
			{
				continue;
			}

			occupiedKeys.Add(identity.Key);

			var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[ObsConfigKeys.Host] = JsonString(host),
				[ObsConfigKeys.Port] = JsonString(port.ToString(CultureInfo.InvariantCulture)),
				[ObsConfigurationMetadata.VariableIdentityKey] =
					JsonString(ObsConfigurationMetadata.SerializeIdentity(identity)),
				[ObsConfigurationMetadata.SchemaKey] = JsonString(ObsConfigurationMetadata.SchemaVersion)
			};

			var secrets = new Dictionary<string, MigratedSecret>(StringComparer.Ordinal);
			var password = ReadCredential(credentials, "password");
			if (!string.IsNullOrEmpty(password))
			{
				secrets[ObsConfigKeys.Password] = new MigratedSecret(password, MigratedSecretKind.Secret);
			}

			results.Add(new MigratedConfiguration(ObsIntegration.IntegrationId, title, values, secrets));
		}

		return results;
	}

	private static ActionMigrationResult? MigrateSetScene(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var scene = ReadString(config, "SceneName");
		if (string.IsNullOrWhiteSpace(scene))
		{
			return null;
		}

		return new ActionMigrationResult(ObsIntegration.IntegrationId,
			"set-scene",
			action.DisplayName ?? "Set OBS scene",
			Parameters((SceneActionDefinition.SceneParameter, JsonString(scene))),
			ConnectionWarning(ReadString(config, "ConnectionName")));
	}

	private static ActionMigrationResult? MigrateSourceVisibility(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var scene = ReadString(config, "SceneName");
		var source = ReadString(config, "SourceName");
		if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(source))
		{
			return null;
		}

		var mode = (ReadEnumIndex(config, "Method", VisibilityMethodNames) ?? ToggleIndex) switch
		{
			0 => "hide",
			1 => "show",
			_ => "toggle"
		};

		return new ActionMigrationResult(ObsIntegration.IntegrationId,
			"set-source-visibility",
			action.DisplayName ?? "Set OBS source visibility",
			Parameters((SourceVisibilityActionDefinition.SceneParameter, JsonString(scene)),
				(SourceVisibilityActionDefinition.SourceParameter, JsonString(source)),
				(SourceVisibilityActionDefinition.ModeParameter, JsonString(mode))),
			ConnectionWarning(ReadString(config, "ConnectionName")));
	}

	private static ActionMigrationResult? MigrateSetAudioMuted(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var input = ReadString(config, "SourceName");
		if (string.IsNullOrWhiteSpace(input))
		{
			return null;
		}

		var mode = (ReadEnumIndex(config, "Method", AudioMethodNames) ?? ToggleIndex) switch
		{
			0 => "mute",
			1 => "unmute",
			_ => "toggle"
		};

		return new ActionMigrationResult(ObsIntegration.IntegrationId,
			"set-input-mute",
			action.DisplayName ?? "Set OBS input mute",
			Parameters((MuteInputActionDefinition.InputParameter, JsonString(input)),
				(MuteInputActionDefinition.ModeParameter, JsonString(mode))),
			ConnectionWarning(ReadString(config, "ConnectionName")));
	}

	private static ActionMigrationResult? MigrateSetFilterState(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var filter = ReadString(config, "FilterName");
		var sceneName = ReadString(config, "SceneName");
		var sourceName = ReadString(config, "SourceName");

		// Mirrors SetFilterStateAction.Trigger's own fallback: an empty source name means the filter lives
		// directly on the scene, so the scene itself is the target.
		var target = string.IsNullOrWhiteSpace(sourceName) ? sceneName : sourceName;
		if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(filter))
		{
			return null;
		}

		var mode = (ReadEnumIndex(config, "Method", VisibilityMethodNames) ?? ToggleIndex) switch
		{
			0 => "disable",
			1 => "enable",
			_ => "toggle"
		};

		return new ActionMigrationResult(ObsIntegration.IntegrationId,
			"set-source-filter",
			action.DisplayName ?? "Set OBS source filter",
			Parameters((SetSourceFilterActionDefinition.SourceParameter, JsonString(target)),
				(SetSourceFilterActionDefinition.FilterParameter, JsonString(filter)),
				(SetSourceFilterActionDefinition.ModeParameter, JsonString(mode))),
			ConnectionWarning(ReadString(config, "ConnectionName")));
	}

	private static ActionMigrationResult MigrateSaveReplayBuffer(ForeignAction action)
	{
		var connectionName = TryParseConfig(action.Configuration, out var config)
			? ReadString(config, "ConnectionName")
			: null;

		return new ActionMigrationResult(ObsIntegration.IntegrationId,
			"save-replay-buffer",
			action.DisplayName ?? "Save OBS replay buffer",
			Parameters(),
			ConnectionWarning(connectionName));
	}

	private static ActionMigrationResult? MigrateGenericState(ForeignAction action, string category)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var actionId = (ReadEnumIndex(config, "Method", StateMethodNames) ?? ToggleIndex) switch
		{
			0 => $"start-{category}",
			1 => $"stop-{category}",
			_ => $"toggle-{category}"
		};

		return new ActionMigrationResult(ObsIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? DefaultStateLabel(actionId),
			Parameters(),
			ConnectionWarning(ReadString(config, "ConnectionName")));
	}

	private static string DefaultStateLabel(string actionId) => actionId switch
	{
		"start-recording" => "Start OBS recording",
		"stop-recording" => "Stop OBS recording",
		"toggle-recording" => "Toggle OBS recording",
		"start-streaming" => "Start OBS streaming",
		"stop-streaming" => "Stop OBS streaming",
		"toggle-streaming" => "Toggle OBS streaming",
		"start-virtual-camera" => "Start OBS virtual camera",
		"stop-virtual-camera" => "Stop OBS virtual camera",
		"toggle-virtual-camera" => "Toggle OBS virtual camera",
		"start-replay-buffer" => "Start OBS replay buffer",
		"stop-replay-buffer" => "Stop OBS replay buffer",
		"toggle-replay-buffer" => "Toggle OBS replay buffer",
		_ => "OBS action"
	};

	private static IReadOnlyList<LocalizedText> ConnectionWarning(string? connectionName)
		=>
		[
			string.IsNullOrWhiteSpace(connectionName)
				? AppStrings.Migration.Warning.Obs.DefaultConnectionNotMigrated()
				: AppStrings.Migration.Warning.Obs.NamedConnectionNotMigrated(name: connectionName)
		];

	private static bool TryParseHost(string raw, out string host, out int port)
	{
		var candidate = raw.Contains("://", StringComparison.Ordinal) ? raw : $"ws://{raw}";
		if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
		{
			host = string.Empty;
			port = 0;
			return false;
		}

		host = uri.Host;
		port = uri.Port > 0 ? uri.Port : ObsConfigFlow.DefaultPort;
		return true;
	}

	private static string? ReadCredential(IReadOnlyDictionary<string, string> credentials, string name)
		=> credentials.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
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

	// Newtonsoft.Json serializes an unattributed enum as its underlying integer by default, but a value
	// typed either way is accepted here since nothing in this plugin's source proves which one Macro Deck 2
	// actually wrote.
	private static int? ReadEnumIndex(JsonElement config, string name, string[] names)
	{
		if (!TryGetProperty(config, name, out var value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var index) &&
			index >= 0 &&
			index < names.Length)
		{
			return index;
		}

		if (value.ValueKind == JsonValueKind.String)
		{
			var text = value.GetString();
			for (var i = 0; i < names.Length; i++)
			{
				if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}
		}

		return null;
	}

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
