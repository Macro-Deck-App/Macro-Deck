using System.Text.Json;

namespace MacroDeckHost.Integrations.Discord;

internal static class DiscordStateMapper
{
	public static DiscordState ApplyVoiceSettings(DiscordState state, JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return state;
		}

		return state with
		{
			SelfMuted = ReadBool(data, "mute") ?? state.SelfMuted,
			SelfDeafened = ReadBool(data, "deaf") ?? state.SelfDeafened,
			NoiseSuppression = ReadBool(data, "noise_suppression") ?? state.NoiseSuppression,
			EchoCancellation = ReadBool(data, "echo_cancellation") ?? state.EchoCancellation,
			AutomaticGainControl = ReadBool(data, "automatic_gain_control") ?? state.AutomaticGainControl,
			InputVolume = ReadNestedDouble(data, "input", "volume") ?? state.InputVolume,
			OutputVolume = ReadNestedDouble(data, "output", "volume") ?? state.OutputVolume,
			VoiceMode = ReadString(data, "mode", "type") ?? state.VoiceMode
		};
	}

	public static DiscordState ApplyVoiceState(DiscordState state, JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object || state.UserId is null)
		{
			return state;
		}

		var userId = ReadString(data, "user", "id");
		if (!string.Equals(userId, state.UserId, StringComparison.Ordinal))
		{
			return state;
		}

		if (!data.TryGetProperty("voice_state", out var voiceState) || voiceState.ValueKind != JsonValueKind.Object)
		{
			return state;
		}

		return state with
		{
			ServerMuted = ReadBool(voiceState, "mute") ?? state.ServerMuted,
			ServerDeafened = ReadBool(voiceState, "deaf") ?? state.ServerDeafened
		};
	}

	public static DiscordState ApplySelectedVoiceChannel(DiscordState state, JsonElement data, string? guildName = null)
	{
		if (data.ValueKind is not JsonValueKind.Object)
		{
			return state.WithoutVoiceChannel();
		}

		var updated = state with
		{
			VoiceChannelId = ReadString(data, "id") ?? state.VoiceChannelId,
			VoiceChannelName = ReadString(data, "name") ?? state.VoiceChannelName,
			VoiceGuildId = ReadString(data, "guild_id") ?? state.VoiceGuildId,
			VoiceGuildName = guildName ?? state.VoiceGuildName,
			ServerMuted = false,
			ServerDeafened = false
		};

		if (!data.TryGetProperty("voice_states", out var voiceStates) ||
			voiceStates.ValueKind != JsonValueKind.Array)
		{
			return updated;
		}

		foreach (var entry in voiceStates.EnumerateArray())
		{
			updated = ApplyVoiceState(updated, entry);
		}

		return updated;
	}

	public static DiscordState ApplyVoiceConnectionStatus(DiscordState state, JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return state;
		}

		return state with
		{
			VoiceConnectionState = ReadString(data, "state") ?? state.VoiceConnectionState,
			AveragePing = ReadInt(data, "average_ping") ?? state.AveragePing
		};
	}

	public static (string? Id, string? Name) ReadUser(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty("user", out var user) ||
			user.ValueKind != JsonValueKind.Object)
		{
			return (null, null);
		}

		var name = ReadString(user, "global_name") ?? ReadString(user, "username");
		return (ReadString(user, "id"), name);
	}

	public static IReadOnlyList<string> ReadScopes(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty("scopes", out var scopes) ||
			scopes.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return
		[
			.. scopes.EnumerateArray()
				.Where(s => s.ValueKind == JsonValueKind.String)
				.Select(s => s.GetString()!)
		];
	}

	internal static bool? ReadBool(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;

	internal static int? ReadInt(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var parsed)
				? parsed
				: null;

	internal static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	internal static string? ReadString(JsonElement element, string objectProperty, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(objectProperty, out var nested) &&
			nested.ValueKind == JsonValueKind.Object
				? ReadString(nested, property)
				: null;

	internal static double? ReadNestedDouble(JsonElement element, string objectProperty, string property)
	{
		if (element.ValueKind != JsonValueKind.Object ||
			!element.TryGetProperty(objectProperty, out var nested) ||
			nested.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		return nested.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var parsed)
				? parsed
				: null;
	}
}
