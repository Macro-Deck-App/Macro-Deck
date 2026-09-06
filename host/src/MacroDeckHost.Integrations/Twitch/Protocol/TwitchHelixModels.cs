namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed record TwitchStreamInfo(
	bool IsLive,
	int? ViewerCount,
	string? Title,
	string? CategoryName,
	DateTimeOffset? StartedAt);

internal sealed record TwitchChannelInfo(string? Title, string? CategoryId, string? CategoryName, string? Language);

internal sealed record TwitchSubscriberInfo(int? Count, int? Points);

internal sealed record TwitchCustomReward(string Id, string Title);

internal sealed record TwitchUserInfo(string Id, string Login, string DisplayName);

internal sealed record TwitchActiveEvent(string Id, string Title, IReadOnlyList<TwitchNamedOutcome> Outcomes);

internal sealed record TwitchNamedOutcome(string Id, string Title);

internal sealed record TwitchCreatedClip(string Id, string EditUrl);

internal enum TwitchChatMode
{
	EmoteOnly,
	FollowersOnly,
	SlowMode,
	SubscribersOnly,
	UniqueChat
}

internal enum TwitchSubscriptionResult
{
	Created,

	Duplicate,

	MissingScope,

	Unsupported,

	Failed
}
