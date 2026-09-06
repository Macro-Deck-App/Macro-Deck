using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Twitch;

/// <summary>
/// Translates Macro Deck 2's Twitch plugin into <see cref="TwitchIntegration" />'s actions and
/// configuration entries. Kept out of <see cref="TwitchIntegration" /> itself so that class stays
/// readable; this is only ever called through its <see cref="IIntegrationMigration" /> members.
/// </summary>
internal sealed class TwitchMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	private static readonly int[] AllowedCommercialLengths = [30, 60, 90, 120, 150, 180];

	// Twitch Plugin.csproj's AssemblyName, also the "dll" field of its ExtensionManifest.json minus the
	// extension.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Twitch Plugin"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author ("Macro Deck") and
	// display name ("Twitch Plugin"), lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["macro deck_twitch plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"SuchByte.TwitchPlugin.Actions.ClearChatAction" => Simple(action, "clear-chat", "Clear chat"),
		"SuchByte.TwitchPlugin.Actions.MakeClipAction" => MigrateMakeClip(action),
		"SuchByte.TwitchPlugin.Actions.PlayAdAction" => MigratePlayAd(action),
		"SuchByte.TwitchPlugin.Actions.SendChatMessageAction" => MigrateSendChatMessage(action),
		"SuchByte.TwitchPlugin.Actions.SetEmoteChatAction" => MigrateChatMode(action, "emote-only"),
		"SuchByte.TwitchPlugin.Actions.SetFollowerChatAction" => MigrateFollowerChat(action),
		"SuchByte.TwitchPlugin.Actions.SetSlowChatAction" => MigrateSlowChat(action),
		"SuchByte.TwitchPlugin.Actions.SetSubscriberChatAction" => MigrateChatMode(action, "subscribers-only"),
		"SuchByte.TwitchPlugin.Actions.SetTitleGameAction" => MigrateSetTitleGame(action),
		"SuchByte.TwitchPlugin.Actions.StreamMarkerAction" => Simple(action,
			"create-stream-marker",
			"Create stream marker"),
		_ => null
	};

	// The plugin only ever stored a raw Twitch user access token, never a refresh token, an app client id
	// or the token's expiry - TwitchConfigFlow's device-code flow and TwitchTokenManager's refresh both
	// need those. Migrating the access token alone would read as a connected account until it expires
	// (typically within hours) and then fail with no way to recover it, so nothing is migrated; the user
	// reconnects Twitch through the normal config flow instead.
	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings) => [];

	private static ActionMigrationResult? MigrateMakeClip(ForeignAction action)
		=> new(TwitchIntegration.IntegrationId,
			"create-clip",
			action.DisplayName ?? "Create clip",
			Parameters(("hasDelay", JsonSerializer.SerializeToElement(false))));

	private static ActionMigrationResult? MigratePlayAd(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var length = ReadInt(config, "Length") ?? 30;
		var closest = AllowedCommercialLengths.OrderBy(candidate => Math.Abs(candidate - length)).First();
		IReadOnlyList<LocalizedText>? warnings = closest == length
			? null
			: [AppStrings.Migration.Warning.Twitch.CommercialLengthRounded(length: length, seconds: closest)];

		return new ActionMigrationResult(TwitchIntegration.IntegrationId,
			"run-commercial",
			action.DisplayName ?? "Run commercial",
			Parameters(("length", JsonSerializer.SerializeToElement(closest.ToString(CultureInfo.InvariantCulture)))),
			warnings);
	}

	private static ActionMigrationResult? MigrateSendChatMessage(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var message = ReadString(config, "Message");
		if (string.IsNullOrWhiteSpace(message))
		{
			return null;
		}

		return new ActionMigrationResult(TwitchIntegration.IntegrationId,
			"send-chat-message",
			action.DisplayName ?? "Send chat message",
			Parameters(("message", JsonString(message))));
	}

	private static ActionMigrationResult? MigrateChatMode(ForeignAction action, string mode)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		// "Toggle" flips whatever the chat mode currently is at trigger time; Macro Deck 3's chat-mode
		// action always sets a fixed on/off value, so there is no equivalent without changing behaviour.
		var method = ReadEnumIndex(config, "Method", MethodOn, MethodOff, MethodToggle);
		if (method is not (0 or 1))
		{
			return null;
		}

		return new ActionMigrationResult(TwitchIntegration.IntegrationId,
			"set-chat-mode",
			action.DisplayName ?? "Set chat mode",
			Parameters(("mode", JsonString(mode)),
				("enabled", JsonSerializer.SerializeToElement(method == 0))));
	}

	private static ActionMigrationResult? MigrateFollowerChat(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var method = ReadEnumIndex(config, "Method", MethodOn, MethodOff, MethodToggle);
		if (method is not (0 or 1))
		{
			return null;
		}

		var seconds = ReadDouble(config, "RequiredFollowTime") ?? 600;
		var parameters = method == 0
			? Parameters(("mode", JsonString("followers-only")),
				("enabled", JsonSerializer.SerializeToElement(true)),
				("duration", JsonSerializer.SerializeToElement((int)Math.Round(seconds / 60))))
			: Parameters(("mode", JsonString("followers-only")),
				("enabled", JsonSerializer.SerializeToElement(false)));

		return new ActionMigrationResult(TwitchIntegration.IntegrationId,
			"set-chat-mode",
			action.DisplayName ?? "Set chat mode",
			parameters);
	}

	private static ActionMigrationResult? MigrateSlowChat(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var method = ReadEnumIndex(config, "Method", MethodOn, MethodOff, MethodToggle);
		if (method is not (0 or 1))
		{
			return null;
		}

		var seconds = ReadDouble(config, "MessageCooldown") ?? 30;
		var parameters = method == 0
			? Parameters(("mode", JsonString("slow")),
				("enabled", JsonSerializer.SerializeToElement(true)),
				("duration", JsonSerializer.SerializeToElement((int)Math.Round(seconds))))
			: Parameters(("mode", JsonString("slow")),
				("enabled", JsonSerializer.SerializeToElement(false)));

		return new ActionMigrationResult(TwitchIntegration.IntegrationId,
			"set-chat-mode",
			action.DisplayName ?? "Set chat mode",
			parameters);
	}

	private static ActionMigrationResult MigrateSetTitleGame(ForeignAction action)
	{
		var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (TryParseConfig(action.Configuration, out var config))
		{
			if (ReadBool(config, "UseStreamTitle") ?? true)
			{
				if (ReadString(config, "StreamTitle") is { Length: > 0 } title)
				{
					parameters["title"] = JsonString(title);
				}
			}

			if (ReadBool(config, "UseGame") ?? true)
			{
				if (ReadString(config, "Game") is { Length: > 0 } game)
				{
					parameters["category"] = JsonString(game);
				}
			}
		}

		return new ActionMigrationResult(TwitchIntegration.IntegrationId,
			"set-stream-info",
			action.DisplayName ?? "Set stream title/game",
			parameters);
	}

	private static ActionMigrationResult Simple(ForeignAction action, string actionId, string label)
		=> new(TwitchIntegration.IntegrationId, actionId, action.DisplayName ?? label, Parameters());

	private const string MethodOn = "On";
	private const string MethodOff = "Off";
	private const string MethodToggle = "Toggle";

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
		=> TryGetProperty(config, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;

	private static int? ReadInt(JsonElement config, string name)
		=> TryGetProperty(config, name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var number)
				? number
				: null;

	private static double? ReadDouble(JsonElement config, string name)
		=> TryGetProperty(config, name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number)
				? number
				: null;

	// System.Text.Json serializes an unattributed enum as its underlying integer, but the enum's own
	// member names are accepted too since nothing proves a future plugin version couldn't switch to a
	// string converter.
	private static int? ReadEnumIndex(JsonElement config, string name, params string[] names)
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
