namespace MacroDeckHost.Application.Twitch.Chat;

public static class TwitchChatWidgetType
{
	public const string OwnerId = "app.macro-deck.twitch";

	public const string LocalId = "chat";

	public const string QualifiedId = OwnerId + "::" + LocalId;

	public const string AccountKey = "account";
}

public sealed record TwitchChatAccount(string UserId, string Label);

public enum TwitchChatFragmentKind
{
	Text,

	Emote,

	Mention,

	Cheermote
}

public sealed record TwitchChatFragment(TwitchChatFragmentKind Kind, string Text, string? EmoteId = null);

public sealed record TwitchChatBadge(string SetId, string Id, string? ImageUrl);

public sealed record TwitchChatMessage(
	string MessageId,
	string ChatterId,
	string ChatterLogin,
	string ChatterName,
	string Color,
	IReadOnlyList<TwitchChatBadge> Badges,
	IReadOnlyList<TwitchChatFragment> Fragments)
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
			if (fragment is { Kind: TwitchChatFragmentKind.Emote, EmoteId: { } emoteId })
			{
				yield return TwitchChatImage.Emote(emoteId);
			}
		}
	}
}

public sealed record TwitchChatSnapshot(
	TwitchChatAccount? Account,
	bool IsConnected,
	IReadOnlyList<TwitchChatMessage> Messages,
	long Version)
{
	public static TwitchChatSnapshot None { get; } = new(null, false, [], 0);
}

public abstract record TwitchChatEvent;

public sealed record TwitchChatMessageReceived(string AccountId, TwitchChatMessage Message) : TwitchChatEvent;

public sealed record TwitchChatMessageDeleted(string AccountId, string MessageId) : TwitchChatEvent;

public sealed record TwitchChatCleared(string AccountId) : TwitchChatEvent;

public sealed record TwitchChatUserCleared(string AccountId, string UserId) : TwitchChatEvent;

public sealed record TwitchChatConnectionChanged(string AccountId, bool IsConnected) : TwitchChatEvent;

internal sealed record TwitchChatImageResolved(TwitchChatImage Image) : TwitchChatEvent;

public sealed class TwitchChatChangedEventArgs : EventArgs
{
	public TwitchChatChangedEventArgs(string? accountId)
	{
		AccountId = accountId;
	}

	// Null when the set of accounts changed, which every session has to re-resolve.
	public string? AccountId { get; }
}
