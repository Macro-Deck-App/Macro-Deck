using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.Streamerbot.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Streamerbot;

/// <summary>
/// Translates Macro Deck 2's two Streamer.bot plugins - MrVibesRSA's "Streamer.bot Plugin" and
/// dichternebel's "Yet another Streamer.bot" - into <see cref="StreamerbotIntegration" />'s "do-action"
/// action and configuration entries. Kept out of <see cref="StreamerbotIntegration" /> itself so that
/// class stays readable; this is only ever called through its <see cref="IIntegrationMigration" />
/// members.
/// </summary>
internal sealed class StreamerbotMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	// MrVibesRSA.StreamerbotPlugin.csproj's AssemblyName and dichternebel.YaSB.csproj's AssemblyName -
	// both also the "dll" field of their respective ExtensionManifest.json minus the extension.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Streamer.botPlugin", "Yet another Streamer.Bot"];

	// Macro Deck 2 derives its settings/credentials file name from each plugin's author and display name,
	// lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } =
		["mrvibes_rsa_streamer.bot plugin", "dichternebel_yet another streamer.bot"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"MrVibesRSA.StreamerbotPlugin.Actions.StreamerBotAction" => MigrateMrVibes(action),
		"dichternebel.YaSB.MacroDeckPlug.YaSBAction" => MigrateYaSB(action),
		_ => null
	};

	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings config)
	{
		// dichternebel's plugin never wrote plugin settings or credentials - it has no server connection
		// of its own to migrate.
		if (!string.Equals(config.SettingsSource,
			"mrvibes_rsa_streamer.bot plugin",
			StringComparison.OrdinalIgnoreCase))
		{
			return [];
		}

		// Macro Deck 2's plugin stores one JSON-serialized profile per credentials entry (key = profile
		// id, value = the profile), and supports any number of Streamer.bot server connections at once.
		// Macro Deck 3's Streamer.bot integration only supports a single connection
		// (AllowsMultipleConfigurations is false), so at most one profile can be carried over; the one
		// marked to auto-connect wins, or the first profile otherwise, and any others are dropped.
		var profiles = config.Credentials
			.SelectMany(dict => dict.Values)
			.Select(TryParseProfile)
			.OfType<StreamerbotProfile>()
			.ToList();

		var chosen = profiles.FirstOrDefault(p => p.AutoConnect) ?? profiles.FirstOrDefault();
		if (chosen is null || string.IsNullOrWhiteSpace(chosen.Address))
		{
			return [];
		}

		var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[StreamerbotConfigKeys.Host] = JsonSerializer.SerializeToElement(chosen.Address),
			[StreamerbotConfigKeys.Port] = JsonSerializer.SerializeToElement(chosen.Port ?? "8080"),
			[StreamerbotConfigKeys.Endpoint] = JsonSerializer.SerializeToElement(chosen.Endpoint ?? string.Empty)
		};

		var secrets = new Dictionary<string, MigratedSecret>(StringComparer.Ordinal);
		if (!string.IsNullOrEmpty(chosen.Password))
		{
			secrets[StreamerbotConfigKeys.Password] = new MigratedSecret(chosen.Password, MigratedSecretKind.Password);
		}

		var title = string.IsNullOrWhiteSpace(chosen.Name) ? "Streamer.bot" : chosen.Name;
		return [new MigratedConfiguration(StreamerbotIntegration.IntegrationId, title, values, secrets)];
	}

	private static ActionMigrationResult? MigrateMrVibes(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var actionId = ReadString(config, "actionId");
		if (string.IsNullOrWhiteSpace(actionId))
		{
			return null;
		}

		var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[DoActionActionDefinition.ActionParameterName] = JsonSerializer.SerializeToElement(actionId)
		};

		// The plugin only ever supports one unnamed argument, sent to Streamer.bot under the fixed key
		// "key" (WebSocketService.DoAction's args = new { key = value }).
		if (ReadString(config, "actionArgument") is { Length: > 0 } argument)
		{
			parameters[DoActionActionDefinition.ArgumentsParameterName] =
				JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["key"] = argument });
		}

		var profile = ReadString(config, "profile");
		IReadOnlyList<LocalizedText>? warnings = string.IsNullOrWhiteSpace(profile)
			? null
			: [AppStrings.Migration.Warning.Streamerbot.ProfileIgnored(profile: profile)];

		return new ActionMigrationResult(StreamerbotIntegration.IntegrationId,
			"do-action",
			action.DisplayName ?? "Run Streamer.bot action",
			parameters,
			warnings);
	}

	private static ActionMigrationResult? MigrateYaSB(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var actionId = ReadString(config, "streamerBotActionId");
		if (string.IsNullOrWhiteSpace(actionId))
		{
			return null;
		}

		var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[DoActionActionDefinition.ActionParameterName] = JsonSerializer.SerializeToElement(actionId)
		};

		List<LocalizedText>? warnings = null;
		var rawArgument = ReadString(config, "streamerBotActionArgument");
		if (!string.IsNullOrWhiteSpace(rawArgument))
		{
			// The plugin deserializes this field as a raw JSON object and sends it as Streamer.bot's
			// "args" verbatim (WebSocketClient.SendMessageAsync), the same shape Macro Deck 3's arguments
			// parameter expects.
			if (TryParseArguments(rawArgument, out var arguments))
			{
				parameters[DoActionActionDefinition.ArgumentsParameterName] = arguments;
			}
			else
			{
				warnings = [AppStrings.Migration.Warning.Streamerbot.ArgumentsNotParsed()];
			}
		}

		return new ActionMigrationResult(StreamerbotIntegration.IntegrationId,
			"do-action",
			action.DisplayName ?? "Run Streamer.bot action",
			parameters,
			warnings);
	}

	private static bool TryParseArguments(string rawArgument, out JsonElement arguments)
	{
		try
		{
			var parsed = JsonDocument.Parse(rawArgument).RootElement.Clone();
			if (parsed.ValueKind == JsonValueKind.Object)
			{
				arguments = parsed;
				return true;
			}
		}
		catch (JsonException)
		{
			// fall through
		}

		arguments = default;
		return false;
	}

	private sealed record StreamerbotProfile(
		string? Name,
		string? Address,
		string? Port,
		string? Endpoint,
		string? Password,
		bool AutoConnect);

	private static StreamerbotProfile? TryParseProfile(string json)
	{
		if (!TryParseConfig(json, out var config))
		{
			return null;
		}

		return new StreamerbotProfile(ReadString(config, "Name"),
			ReadString(config, "Address"),
			ReadString(config, "Port"),
			ReadString(config, "Endpoint"),
			ReadString(config, "Password"),
			TryGetProperty(config, "AutoConnect", out var autoConnect) &&
			autoConnect.ValueKind == JsonValueKind.True);
	}

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
}
