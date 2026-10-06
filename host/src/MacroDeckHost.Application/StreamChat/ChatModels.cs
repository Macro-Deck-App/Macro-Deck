namespace MacroDeckHost.Application.StreamChat;

public static class StreamChatWidgetType
{
	public const string LocalId = "chat";

	public const string AccountKey = "account";

	public const string AllowModerationKey = "allowModeration";

	public const string TextSizeKey = "textSize";

	public const string BackgroundColorKey = "backgroundColor";

	public static string QualifiedId(string ownerId) => ownerId + "::" + LocalId;
}

public sealed record ChatAccount(string AccountId, string Label);

public enum ChatFragmentKind
{
	Text,

	Emote,

	Mention,

	Cheermote
}

public sealed record ChatFragment(ChatFragmentKind Kind, string Text, string? EmoteId = null);

public sealed record ChatBadge(string SetId, string Id, string? ImageUrl);

public sealed record ChatMessage(
	string MessageId,
	string AuthorId,
	string AuthorLogin,
	string AuthorName,
	string Color,
	IReadOnlyList<ChatBadge> Badges,
	IReadOnlyList<ChatFragment> Fragments,
	string? SourceChannelId = null,
	string? SourceChannelName = null)
{
	public string PlainText => string.Concat(Fragments.Select(fragment => fragment.Text));

	public IEnumerable<TwitchChatImage> Images()
	{
		foreach (var badge in Badges)
		{
			if (badge.ImageUrl is { } url)
			{
				yield return TwitchChatImage.Badge(badge.SetId, badge.Id, url);
			}
		}

		foreach (var fragment in Fragments)
		{
			if (fragment is { Kind: ChatFragmentKind.Emote, EmoteId: { } emoteId })
			{
				yield return TwitchChatImage.Emote(emoteId);
			}
		}
	}
}

public sealed record ChatSnapshot(
	ChatAccount? Account,
	bool IsConnected,
	IReadOnlyList<ChatMessage> Messages,
	long Version)
{
	public static ChatSnapshot None { get; } = new(null, false, [], 0);
}

public abstract record ChatEvent;

public sealed record ChatMessageReceived(string AccountId, ChatMessage Message) : ChatEvent;

public sealed record ChatMessageDeleted(string AccountId, string MessageId) : ChatEvent;

public sealed record ChatCleared(string AccountId) : ChatEvent;

public sealed record ChatUserCleared(string AccountId, string UserId) : ChatEvent;

public sealed record ChatConnectionChanged(string AccountId, bool IsConnected) : ChatEvent;

internal sealed record ChatImageResolved(TwitchChatImage Image) : ChatEvent;

public sealed class ChatChangedEventArgs : EventArgs
{
	public ChatChangedEventArgs(string? accountId)
	{
		AccountId = accountId;
	}

	// Null when the set of accounts changed, which every session has to re-resolve.
	public string? AccountId { get; }
}
