namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal static class YouTubeBroadcastStatus
{
	public const string Active = "active";
	public const string Upcoming = "upcoming";
}

internal static class YouTubeBroadcastTransition
{
	public const string Live = "live";
	public const string Complete = "complete";
	public const string Testing = "testing";
}

internal static class YouTubeLifeCycleStatus
{
	public const string Complete = "complete";
	public const string Created = "created";
	public const string Live = "live";
	public const string LiveStarting = "liveStarting";
	public const string Ready = "ready";
	public const string Revoked = "revoked";
	public const string TestStarting = "testStarting";
	public const string Testing = "testing";
}

internal static class YouTubeChatMessageTypes
{
	public const string Text = "textMessageEvent";
	public const string SuperChat = "superChatEvent";
	public const string SuperSticker = "superStickerEvent";
	public const string NewSponsor = "newSponsorEvent";
	public const string MemberMilestone = "memberMilestoneChatEvent";
	public const string MembershipGifting = "membershipGiftingEvent";
	public const string GiftMembershipReceived = "giftMembershipReceivedEvent";
	public const string UserBanned = "userBannedEvent";
	public const string MessageDeleted = "messageDeletedEvent";
	public const string MessageRetracted = "messageRetractedEvent";
	public const string Tombstone = "tombstone";
	public const string ChatEnded = "chatEndedEvent";
}

internal sealed record YouTubeBroadcast(
	string Id,
	string Title,
	string? LiveChatId,
	DateTimeOffset? ActualStartTime,
	DateTimeOffset? ScheduledStartTime,
	string LifeCycleStatus,
	bool EnableAutoStart,
	bool EnableMonitorStream);

internal sealed record YouTubeVideoSnippet(
	string Title,
	string Description,
	IReadOnlyList<string> Tags,
	string CategoryId,
	string? DefaultLanguage = null,
	string? DefaultAudioLanguage = null,
	string? ThumbnailUrl = null);

internal sealed record YouTubeVideo(
	string Id,
	YouTubeVideoSnippet Snippet,
	long? LikeCount,
	long? ConcurrentViewers,
	DateTimeOffset? ActualStartTime,
	string? ActiveLiveChatId);

internal sealed record YouTubeChannel(string Id, string Title, string? Handle, long? SubscriberCount);

internal sealed record YouTubeChatPage(
	string? NextPageToken,
	int PollingIntervalMillis,
	DateTimeOffset? OfflineAt,
	IReadOnlyList<YouTubeChatMessage> Items);

internal sealed record YouTubeChatAuthor(
	string ChannelId,
	string DisplayName,
	string? ProfileImageUrl,
	bool IsChatOwner,
	bool IsChatModerator,
	bool IsChatSponsor,
	bool IsVerified);

internal sealed record YouTubeChatMessage(
	string Id,
	string Type,
	DateTimeOffset? PublishedAt,
	string? DisplayMessage,
	YouTubeChatAuthor Author)
{
	public string? TextMessage { get; init; }

	public YouTubeSuperChat? SuperChat { get; init; }

	public YouTubeSuperSticker? SuperSticker { get; init; }

	public YouTubeMemberMilestone? MemberMilestone { get; init; }

	public YouTubeNewSponsor? NewSponsor { get; init; }

	public YouTubeMembershipGifting? MembershipGifting { get; init; }

	public YouTubeUserBanned? UserBanned { get; init; }

	public string? DeletedMessageId { get; init; }
}

internal sealed record YouTubeSuperChat(
	long AmountMicros,
	string Currency,
	string AmountDisplayString,
	string? UserComment,
	int Tier);

internal sealed record YouTubeSuperSticker(
	long AmountMicros,
	string Currency,
	string AmountDisplayString,
	int Tier,
	string? StickerId,
	string? AltText);

internal sealed record YouTubeMemberMilestone(string? UserComment, int? MemberMonth, string? MemberLevelName);

internal sealed record YouTubeNewSponsor(string? MemberLevelName, bool IsUpgrade);

internal sealed record YouTubeMembershipGifting(int GiftMembershipsCount, string? GiftMembershipsLevelName);

internal sealed record YouTubeUserBanned(
	string BannedChannelId,
	string? BannedDisplayName,
	string BanType,
	long? BanDurationSeconds);
