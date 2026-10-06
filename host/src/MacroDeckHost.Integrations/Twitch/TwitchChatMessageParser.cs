using System.Text.Json;
using MacroDeckHost.Application.StreamChat;

namespace MacroDeckHost.Integrations.Twitch;

internal static class TwitchChatMessageParser
{
	public const string MessageType = "channel.chat.message";
	public const string MessageDeleteType = "channel.chat.message_delete";
	public const string ClearType = "channel.chat.clear";
	public const string ClearUserMessagesType = "channel.chat.clear_user_messages";

	public static ChatEvent? ToChatEvent(
		string accountId,
		string? subscriptionType,
		JsonElement payload,
		TwitchChatBadgeMap badges)
		=> subscriptionType switch
		{
			MessageType => Parse(payload, badges) is { } message
				? new ChatMessageReceived(accountId, message)
				: null,
			MessageDeleteType => ReadString(payload, "message_id") is { Length: > 0 } messageId
				? new ChatMessageDeleted(accountId, messageId)
				: null,
			ClearType => new ChatCleared(accountId),
			ClearUserMessagesType => ReadString(payload, "target_user_id") is { Length: > 0 } userId
				? new ChatUserCleared(accountId, userId)
				: null,
			_ => null
		};

	public static ChatMessage? Parse(JsonElement payload, TwitchChatBadgeMap badges)
	{
		if (payload.ValueKind is not JsonValueKind.Object ||
			ReadString(payload, "message_id") is not { Length: > 0 } messageId ||
			ReadString(payload, "chatter_user_id") is not { Length: > 0 } chatterId)
		{
			return null;
		}

		var login = ReadString(payload, "chatter_user_login") ?? string.Empty;
		var name = ReadString(payload, "chatter_user_name") is { Length: > 0 } displayName ? displayName : login;

		return new ChatMessage(messageId,
			chatterId,
			login,
			name,
			ChatStyle.NormalizeColor(ReadString(payload, "color"), chatterId),
			ReadBadges(payload, badges),
			ReadFragments(payload),
			ReadString(payload, "source_broadcaster_user_id") is { Length: > 0 } sourceId ? sourceId : null,
			ReadString(payload, "source_broadcaster_user_name") is { Length: > 0 } sourceName
				? sourceName
				: ReadString(payload, "source_broadcaster_user_login"));
	}

	private static List<ChatBadge> ReadBadges(JsonElement payload, TwitchChatBadgeMap badges)
	{
		var result = new List<ChatBadge>();

		if (!payload.TryGetProperty("badges", out var list) || list.ValueKind is not JsonValueKind.Array)
		{
			return result;
		}

		foreach (var badge in list.EnumerateArray())
		{
			if (ReadString(badge, "set_id") is { Length: > 0 } setId && ReadString(badge, "id") is { Length: > 0 } id)
			{
				result.Add(new ChatBadge(setId, id, badges.Find(setId, id)));
			}
		}

		return result;
	}

	private static List<ChatFragment> ReadFragments(JsonElement payload)
	{
		var fragments = new List<ChatFragment>();

		if (!payload.TryGetProperty("message", out var message) || message.ValueKind is not JsonValueKind.Object)
		{
			return fragments;
		}

		if (message.TryGetProperty("fragments", out var list) && list.ValueKind is JsonValueKind.Array)
		{
			foreach (var fragment in list.EnumerateArray())
			{
				if (ReadFragment(fragment) is { } parsed)
				{
					fragments.Add(parsed);
				}
			}
		}

		if (fragments.Count == 0 && ReadString(message, "text") is { Length: > 0 } text)
		{
			fragments.Add(new ChatFragment(ChatFragmentKind.Text, text));
		}

		return fragments;
	}

	private static ChatFragment? ReadFragment(JsonElement fragment)
	{
		if (fragment.ValueKind is not JsonValueKind.Object || ReadString(fragment, "text") is not { Length: > 0 } text)
		{
			return null;
		}

		return ReadString(fragment, "type") switch
		{
			"emote" when fragment.TryGetProperty("emote", out var emote) &&
				ReadString(emote, "id") is { Length: > 0 } emoteId
				=> new ChatFragment(ChatFragmentKind.Emote, text, emoteId),
			"mention" => new ChatFragment(ChatFragmentKind.Mention, text),
			"cheermote" => new ChatFragment(ChatFragmentKind.Cheermote, text),
			_ => new ChatFragment(ChatFragmentKind.Text, text)
		};
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind is JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind is JsonValueKind.String
				? value.GetString()
				: null;
}

internal sealed class TwitchChatBadgeMap
{
	public static TwitchChatBadgeMap Empty { get; } = new([], []);

	private readonly Dictionary<(string SetId, string Id), string> _urls;

	// Channel badges win over global ones with the same set and version, as they do on Twitch.
	public TwitchChatBadgeMap(
		IReadOnlyList<Protocol.TwitchChatBadgeImage> global,
		IReadOnlyList<Protocol.TwitchChatBadgeImage> channel)
	{
		_urls = new Dictionary<(string, string), string>();

		foreach (var badge in global.Concat(channel))
		{
			_urls[(badge.SetId, badge.Id)] = badge.ImageUrl;
		}
	}

	public string? Find(string setId, string id) => _urls.GetValueOrDefault((setId, id));
}
