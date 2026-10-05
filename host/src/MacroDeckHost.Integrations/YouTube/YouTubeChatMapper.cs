using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Integrations.YouTube;

internal static class YouTubeChatMapper
{
	public static bool EndsChat(YouTubeChatMessage message)
		=> string.Equals(message.Type, YouTubeChatMessageTypes.ChatEnded, StringComparison.Ordinal);

	public static ChatEvent? ToChatEvent(string accountId, YouTubeChatMessage message)
	{
		switch (message.Type)
		{
			case YouTubeChatMessageTypes.Text:
			case YouTubeChatMessageTypes.SuperChat:
			case YouTubeChatMessageTypes.SuperSticker:
			case YouTubeChatMessageTypes.MemberMilestone:
			case YouTubeChatMessageTypes.NewSponsor:
			case YouTubeChatMessageTypes.MembershipGifting:
				return LineText(message) is { Length: > 0 } text
					? new ChatMessageReceived(accountId, Line(message, text))
					: null;

			case YouTubeChatMessageTypes.UserBanned when message.UserBanned is { BannedChannelId.Length: > 0 } banned:
				return new ChatUserCleared(accountId, banned.BannedChannelId);

			case YouTubeChatMessageTypes.MessageDeleted:
			case YouTubeChatMessageTypes.MessageRetracted:
			case YouTubeChatMessageTypes.Tombstone:
				return new ChatMessageDeleted(accountId, message.DeletedMessageId ?? message.Id);

			default:
				return null;
		}
	}

	private static ChatMessage Line(YouTubeChatMessage message, string text)
		=> new(message.Id,
			message.Author.ChannelId,
			message.Author.ChannelId,
			message.Author.DisplayName,
			ChatStyle.DefaultColor(message.Author.ChannelId),
			[],
			[new ChatFragment(ChatFragmentKind.Text, text)]);

	private static string? LineText(YouTubeChatMessage message)
	{
		var display = message.DisplayMessage is { Length: > 0 } shown
			? shown
			: message.TextMessage ?? message.SuperSticker?.AltText;

		var amount = message.SuperChat?.AmountDisplayString ?? message.SuperSticker?.AmountDisplayString;

		return amount is { Length: > 0 }
			? string.IsNullOrEmpty(display) ? amount : $"{amount} {display}"
			: display;
	}
}
