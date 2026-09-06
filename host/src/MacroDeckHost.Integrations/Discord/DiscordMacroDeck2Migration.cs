using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.Discord.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord;

/// <summary>Translates the Macro Deck 2 "Discord Plugin" (RecklessBoon.DiscordPlugin) into this integration.</summary>
internal sealed class DiscordMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Discord Plugin"];

	// Macro Deck 2 names a plugin's settings/credentials files "{author}_{assembly}" lowercased; this
	// plugin's manifest author is "RecklessBoon" and its assembly is "Discord Plugin".
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["recklessboon_discord plugin"];

	private const string MuteActionId = "mute";
	private const string DeafenActionId = "deafen";
	private const string ClearRichPresenceActionId = "clear-rich-presence";
	private const string SetRichPresenceActionId = "set-rich-presence";
	private const string ExecuteWebhookActionId = "execute-webhook";

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action)
		=> action.TypeName switch
		{
			"RecklessBoon.MacroDeck.Discord.Actions.SetMuteOnAction" =>
				VoiceState(action, MuteActionId, "Mute", DiscordActionParameters.StateOn),
			"RecklessBoon.MacroDeck.Discord.Actions.SetMuteOffAction" =>
				VoiceState(action, MuteActionId, "Mute", DiscordActionParameters.StateOff),
			"RecklessBoon.MacroDeck.Discord.Actions.ToggleMuteAction" =>
				VoiceState(action, MuteActionId, "Mute", DiscordActionParameters.StateToggle),
			"RecklessBoon.MacroDeck.Discord.Actions.SetDeafenOnAction" =>
				VoiceState(action, DeafenActionId, "Deafen", DiscordActionParameters.StateOn),
			"RecklessBoon.MacroDeck.Discord.Actions.SetDeafenOffAction" =>
				VoiceState(action, DeafenActionId, "Deafen", DiscordActionParameters.StateOff),
			"RecklessBoon.MacroDeck.Discord.Actions.ToggleDeafenAction" =>
				VoiceState(action, DeafenActionId, "Deafen", DiscordActionParameters.StateToggle),
			"RecklessBoon.MacroDeck.Discord.Actions.ClearRichPresenceAction" => new ActionMigrationResult(
				DiscordIntegration.IntegrationId,
				ClearRichPresenceActionId,
				action.DisplayName ?? "Clear Rich Presence",
				Parameters()),
			"RecklessBoon.MacroDeck.Discord.Actions.SetRichPresenceAction" => MigrateRichPresence(action),
			"RecklessBoon.MacroDeck.Discord.Actions.ExecuteWebhookAction" => MigrateWebhook(action),
			_ => null
		};

	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings)
	{
		string? clientId = null;
		if (settings.Settings.TryGetValue("config", out var raw) && TryParseObject(raw, out var config))
		{
			clientId = ReadString(config, "ClientId");
		}

		var clientSecret = settings.Credentials
			.Select(set => set.GetValueOrDefault("client_secret"))
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

		if (clientId is null && clientSecret is null)
		{
			return [];
		}

		var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (clientId is not null)
		{
			values[DiscordConfigKeys.ClientId] = JsonString(clientId);
		}

		var secrets = new Dictionary<string, MigratedSecret>(StringComparer.Ordinal);
		if (clientSecret is not null)
		{
			secrets[DiscordConfigKeys.ClientSecret] = new MigratedSecret(clientSecret, MigratedSecretKind.Secret);
		}

		// Macro Deck 2 authorizes fresh through Discord's local RPC every session and never stored an
		// access or refresh token, so this entry needs the user to reauthorize once before it connects.
		return [new MigratedConfiguration(DiscordIntegration.IntegrationId, "Discord", values, secrets)];
	}

	private static ActionMigrationResult VoiceState(
		ForeignAction action,
		string actionId,
		string fallbackLabel,
		string state)
		=> new(DiscordIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? fallbackLabel,
			Parameters((DiscordActionParameters.StateParameter, JsonString(state))));

	private static ActionMigrationResult? MigrateRichPresence(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var details = ReadString(config, "Details");
		if (details is null)
		{
			return null;
		}

		var parameters = new List<(string Name, JsonElement Value)>
		{
			(SetRichPresenceActionDefinition.DetailsParameter, JsonString(details))
		};

		AddIfPresent(parameters, config, "State", SetRichPresenceActionDefinition.StateParameter);
		AddIfPresent(parameters, config, "LargeImageKey", SetRichPresenceActionDefinition.LargeImageParameter);
		AddIfPresent(parameters, config, "LargeImageText", SetRichPresenceActionDefinition.LargeTextParameter);
		AddIfPresent(parameters, config, "SmallImageKey", SetRichPresenceActionDefinition.SmallImageParameter);
		AddIfPresent(parameters, config, "SmallImageText", SetRichPresenceActionDefinition.SmallTextParameter);

		List<LocalizedText>? warnings = null;
		var hadTiming = HasNonNullProperty(config, "DelayStart") || HasNonNullProperty(config, "Duration");
		if (hadTiming)
		{
			parameters.Add((SetRichPresenceActionDefinition.ElapsedParameter, JsonSerializer.SerializeToElement(true)));
			warnings = [AppStrings.Migration.Warning.Discord.RichPresenceTimingNotMigrated()];
		}
		else
		{
			parameters.Add((SetRichPresenceActionDefinition.ElapsedParameter,
				JsonSerializer.SerializeToElement(false)));
		}

		return new ActionMigrationResult(DiscordIntegration.IntegrationId,
			SetRichPresenceActionId,
			action.DisplayName ?? "Set Rich Presence",
			Parameters(parameters.ToArray()),
			warnings);
	}

	private static ActionMigrationResult? MigrateWebhook(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var parameters = new List<(string Name, JsonElement Value)>();
		AddIfPresent(parameters, config, "message", ExecuteWebhookActionDefinition.ContentParameter);
		AddIfPresent(parameters, config, "name", ExecuteWebhookActionDefinition.UsernameParameter);
		AddIfPresent(parameters, config, "avatar_url", ExecuteWebhookActionDefinition.AvatarParameter);

		var warnings = new List<LocalizedText>();
		if (ReadString(config, "url") is not null)
		{
			warnings.Add(AppStrings.Migration.Warning.Discord.WebhookUrlNotMigrated());
		}

		if (TryReadEmbeds(config, out var embeds))
		{
			AddEmbedParameters(embeds[0], parameters, warnings);
			if (embeds.Count > 1)
			{
				warnings.Add(AppStrings.Migration.Warning.Discord.EmbedsDropped(count: embeds.Count - 1));
			}
		}

		return new ActionMigrationResult(DiscordIntegration.IntegrationId,
			ExecuteWebhookActionId,
			action.DisplayName ?? "Execute Webhook",
			Parameters(parameters.ToArray()),
			warnings);
	}

	private static void AddEmbedParameters(
		JsonElement embed,
		List<(string Name, JsonElement Value)> parameters,
		List<LocalizedText> warnings)
	{
		AddIfPresent(parameters, embed, "Title", ExecuteWebhookActionDefinition.EmbedTitleParameter);
		AddIfPresent(parameters, embed, "Description", ExecuteWebhookActionDefinition.EmbedDescriptionParameter);
		AddIfPresent(parameters, embed, "Url", ExecuteWebhookActionDefinition.EmbedUrlParameter);
		AddIfPresent(parameters, embed, "Image", "Url", ExecuteWebhookActionDefinition.EmbedImageParameter);
		AddIfPresent(parameters, embed, "Thumbnail", "Url", ExecuteWebhookActionDefinition.EmbedThumbnailParameter);
		AddIfPresent(parameters, embed, "Footer", "Text", ExecuteWebhookActionDefinition.EmbedFooterParameter);

		var color = ReadColor(embed);
		if (color is not null)
		{
			parameters.Add((ExecuteWebhookActionDefinition.EmbedColorParameter, JsonString(color)));
		}

		if (ReadNestedString(embed, "Footer", "IconUrl") is not null)
		{
			warnings.Add(AppStrings.Migration.Warning.Discord.EmbedFooterIconDropped());
		}

		if (HasNonNullProperty(embed, "Author") && ReadNestedString(embed, "Author", "Name") is not null)
		{
			warnings.Add(AppStrings.Migration.Warning.Discord.EmbedAuthorDropped());
		}

		if (embed.TryGetProperty("Fields", out var fields) &&
			fields.ValueKind == JsonValueKind.Array &&
			fields.GetArrayLength() > 0)
		{
			warnings.Add(AppStrings.Migration.Warning.Discord.EmbedFieldsDropped());
		}
	}

	private static bool TryReadEmbeds(JsonElement config, out IReadOnlyList<JsonElement> embeds)
	{
		embeds = [];
		if (!config.TryGetProperty("embeds", out var raw) || raw.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		// Macro Deck 2 serializes the embed array to a JSON string and stores that string as the
		// "embeds" property, rather than a nested array - a round trip through its own JObject writer.
		var text = raw.GetString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(text);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				return false;
			}

			embeds =
			[
				.. document.RootElement.EnumerateArray()
					.Where(element => element.ValueKind == JsonValueKind.Object)
					.Select(element => element.Clone())
			];
			return embeds.Count > 0;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static string? ReadColor(JsonElement embed)
	{
		if (!embed.TryGetProperty("Color", out var color) || color.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!TryGetInt(color, "R", out var r) || !TryGetInt(color, "G", out var g) || !TryGetInt(color, "B", out var b))
		{
			return null;
		}

		return $"#{r:x2}{g:x2}{b:x2}";
	}

	private static bool TryGetInt(JsonElement obj, string property, out int value)
	{
		value = 0;
		return obj.TryGetProperty(property, out var element) &&
			element.ValueKind == JsonValueKind.Number &&
			element.TryGetInt32(out value);
	}

	private static void AddIfPresent(
		List<(string Name, JsonElement Value)> parameters,
		JsonElement config,
		string sourceProperty,
		string targetParameter)
	{
		var value = ReadString(config, sourceProperty);
		if (value is not null)
		{
			parameters.Add((targetParameter, JsonString(value)));
		}
	}

	private static void AddIfPresent(
		List<(string Name, JsonElement Value)> parameters,
		JsonElement config,
		string sourceProperty,
		string nestedProperty,
		string targetParameter)
	{
		var value = ReadNestedString(config, sourceProperty, nestedProperty);
		if (value is not null)
		{
			parameters.Add((targetParameter, JsonString(value)));
		}
	}

	private static bool HasNonNullProperty(JsonElement obj, string property)
		=> obj.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null;

	private static string? ReadNestedString(JsonElement obj, string property, string nested)
		=> obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
			? ReadString(value, nested)
			: null;

	private static string? ReadString(JsonElement obj, string property)
	{
		if (!obj.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var text = value.GetString();
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	private static bool TryParseObject(string? json, out JsonElement element)
	{
		element = default;
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}

		try
		{
			element = JsonDocument.Parse(json).RootElement.Clone();
			return element.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
