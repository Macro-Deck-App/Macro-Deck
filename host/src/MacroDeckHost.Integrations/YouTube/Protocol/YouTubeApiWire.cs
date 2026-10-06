using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal sealed class YouTubeListResponse<T>
{
	[JsonPropertyName("nextPageToken")]
	public string? NextPageToken { get; set; }

	[JsonPropertyName("pollingIntervalMillis")]
	public long? PollingIntervalMillis { get; set; }

	[JsonPropertyName("offlineAt")]
	public string? OfflineAt { get; set; }

	[JsonPropertyName("items")]
	public List<T>? Items { get; set; }
}

internal sealed class YouTubeBroadcastWire
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("snippet")]
	public YouTubeBroadcastSnippetWire? Snippet { get; set; }

	[JsonPropertyName("status")]
	public YouTubeBroadcastStatusWire? Status { get; set; }

	[JsonPropertyName("contentDetails")]
	public YouTubeBroadcastContentWire? ContentDetails { get; set; }
}

internal sealed class YouTubeBroadcastSnippetWire
{
	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonPropertyName("liveChatId")]
	public string? LiveChatId { get; set; }

	[JsonPropertyName("actualStartTime")]
	public string? ActualStartTime { get; set; }

	[JsonPropertyName("scheduledStartTime")]
	public string? ScheduledStartTime { get; set; }
}

internal sealed class YouTubeBroadcastStatusWire
{
	[JsonPropertyName("lifeCycleStatus")]
	public string? LifeCycleStatus { get; set; }
}

internal sealed class YouTubeBroadcastContentWire
{
	[JsonPropertyName("enableAutoStart")]
	public bool? EnableAutoStart { get; set; }

	[JsonPropertyName("monitorStream")]
	public YouTubeMonitorStreamWire? MonitorStream { get; set; }
}

internal sealed class YouTubeMonitorStreamWire
{
	[JsonPropertyName("enableMonitorStream")]
	public bool? EnableMonitorStream { get; set; }
}

internal sealed class YouTubeVideoWire
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("snippet")]
	public YouTubeVideoSnippetWire? Snippet { get; set; }

	[JsonPropertyName("statistics")]
	public YouTubeStatisticsWire? Statistics { get; set; }

	[JsonPropertyName("liveStreamingDetails")]
	public YouTubeLiveStreamingDetailsWire? LiveStreamingDetails { get; set; }
}

internal sealed class YouTubeVideoSnippetWire
{
	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonPropertyName("description")]
	public string? Description { get; set; }

	[JsonPropertyName("tags")]
	public List<string>? Tags { get; set; }

	[JsonPropertyName("categoryId")]
	public string? CategoryId { get; set; }

	[JsonPropertyName("defaultLanguage")]
	public string? DefaultLanguage { get; set; }

	[JsonPropertyName("defaultAudioLanguage")]
	public string? DefaultAudioLanguage { get; set; }

	[JsonPropertyName("thumbnails")]
	public Dictionary<string, YouTubeThumbnailWire>? Thumbnails { get; set; }
}

internal sealed class YouTubeThumbnailWire
{
	[JsonPropertyName("url")]
	public string? Url { get; set; }
}

internal sealed class YouTubeStatisticsWire
{
	[JsonPropertyName("likeCount")]
	public long? LikeCount { get; set; }

	[JsonPropertyName("subscriberCount")]
	public long? SubscriberCount { get; set; }

	[JsonPropertyName("hiddenSubscriberCount")]
	public bool? HiddenSubscriberCount { get; set; }
}

internal sealed class YouTubeLiveStreamingDetailsWire
{
	[JsonPropertyName("concurrentViewers")]
	public long? ConcurrentViewers { get; set; }

	[JsonPropertyName("actualStartTime")]
	public string? ActualStartTime { get; set; }

	[JsonPropertyName("activeLiveChatId")]
	public string? ActiveLiveChatId { get; set; }
}

internal sealed class YouTubeChannelWire
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("snippet")]
	public YouTubeChannelSnippetWire? Snippet { get; set; }

	[JsonPropertyName("statistics")]
	public YouTubeStatisticsWire? Statistics { get; set; }
}

internal sealed class YouTubeChannelSnippetWire
{
	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonPropertyName("customUrl")]
	public string? CustomUrl { get; set; }
}

internal sealed class YouTubeChatMessageWire
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("snippet")]
	public YouTubeChatSnippetWire? Snippet { get; set; }

	[JsonPropertyName("authorDetails")]
	public YouTubeChatAuthorWire? AuthorDetails { get; set; }
}

internal sealed class YouTubeChatSnippetWire
{
	[JsonPropertyName("type")]
	public string? Type { get; set; }

	[JsonPropertyName("authorChannelId")]
	public string? AuthorChannelId { get; set; }

	[JsonPropertyName("publishedAt")]
	public string? PublishedAt { get; set; }

	[JsonPropertyName("displayMessage")]
	public string? DisplayMessage { get; set; }

	[JsonPropertyName("textMessageDetails")]
	public YouTubeTextMessageDetailsWire? TextMessageDetails { get; set; }

	[JsonPropertyName("superChatDetails")]
	public YouTubeSuperChatDetailsWire? SuperChatDetails { get; set; }

	[JsonPropertyName("superStickerDetails")]
	public YouTubeSuperStickerDetailsWire? SuperStickerDetails { get; set; }

	[JsonPropertyName("memberMilestoneChatDetails")]
	public YouTubeMemberMilestoneDetailsWire? MemberMilestoneChatDetails { get; set; }

	[JsonPropertyName("newSponsorDetails")]
	public YouTubeNewSponsorDetailsWire? NewSponsorDetails { get; set; }

	[JsonPropertyName("membershipGiftingDetails")]
	public YouTubeMembershipGiftingDetailsWire? MembershipGiftingDetails { get; set; }

	[JsonPropertyName("userBannedDetails")]
	public YouTubeUserBannedDetailsWire? UserBannedDetails { get; set; }

	[JsonPropertyName("messageDeletedDetails")]
	public YouTubeMessageDeletedDetailsWire? MessageDeletedDetails { get; set; }

	[JsonPropertyName("messageRetractedDetails")]
	public YouTubeMessageRetractedDetailsWire? MessageRetractedDetails { get; set; }
}

internal sealed class YouTubeTextMessageDetailsWire
{
	[JsonPropertyName("messageText")]
	public string? MessageText { get; set; }
}

internal sealed class YouTubeSuperChatDetailsWire
{
	[JsonPropertyName("amountMicros")]
	public long? AmountMicros { get; set; }

	[JsonPropertyName("currency")]
	public string? Currency { get; set; }

	[JsonPropertyName("amountDisplayString")]
	public string? AmountDisplayString { get; set; }

	[JsonPropertyName("userComment")]
	public string? UserComment { get; set; }

	[JsonPropertyName("tier")]
	public int? Tier { get; set; }
}

internal sealed class YouTubeSuperStickerDetailsWire
{
	[JsonPropertyName("superStickerMetadata")]
	public YouTubeSuperStickerMetadataWire? SuperStickerMetadata { get; set; }

	[JsonPropertyName("amountMicros")]
	public long? AmountMicros { get; set; }

	[JsonPropertyName("currency")]
	public string? Currency { get; set; }

	[JsonPropertyName("amountDisplayString")]
	public string? AmountDisplayString { get; set; }

	[JsonPropertyName("tier")]
	public int? Tier { get; set; }
}

internal sealed class YouTubeSuperStickerMetadataWire
{
	[JsonPropertyName("stickerId")]
	public string? StickerId { get; set; }

	[JsonPropertyName("altText")]
	public string? AltText { get; set; }
}

internal sealed class YouTubeMemberMilestoneDetailsWire
{
	[JsonPropertyName("userComment")]
	public string? UserComment { get; set; }

	[JsonPropertyName("memberMonth")]
	public int? MemberMonth { get; set; }

	[JsonPropertyName("memberLevelName")]
	public string? MemberLevelName { get; set; }
}

internal sealed class YouTubeNewSponsorDetailsWire
{
	[JsonPropertyName("memberLevelName")]
	public string? MemberLevelName { get; set; }

	[JsonPropertyName("isUpgrade")]
	public bool? IsUpgrade { get; set; }
}

internal sealed class YouTubeMembershipGiftingDetailsWire
{
	[JsonPropertyName("giftMembershipsCount")]
	public int? GiftMembershipsCount { get; set; }

	[JsonPropertyName("giftMembershipsLevelName")]
	public string? GiftMembershipsLevelName { get; set; }
}

internal sealed class YouTubeUserBannedDetailsWire
{
	[JsonPropertyName("bannedUserDetails")]
	public YouTubeBannedUserWire? BannedUserDetails { get; set; }

	[JsonPropertyName("banType")]
	public string? BanType { get; set; }

	[JsonPropertyName("banDurationSeconds")]
	public long? BanDurationSeconds { get; set; }
}

internal sealed class YouTubeBannedUserWire
{
	[JsonPropertyName("channelId")]
	public string? ChannelId { get; set; }

	[JsonPropertyName("displayName")]
	public string? DisplayName { get; set; }
}

internal sealed class YouTubeMessageDeletedDetailsWire
{
	[JsonPropertyName("deletedMessageId")]
	public string? DeletedMessageId { get; set; }
}

internal sealed class YouTubeMessageRetractedDetailsWire
{
	[JsonPropertyName("retractedMessageId")]
	public string? RetractedMessageId { get; set; }
}

internal sealed class YouTubeChatAuthorWire
{
	[JsonPropertyName("channelId")]
	public string? ChannelId { get; set; }

	[JsonPropertyName("displayName")]
	public string? DisplayName { get; set; }

	[JsonPropertyName("profileImageUrl")]
	public string? ProfileImageUrl { get; set; }

	[JsonPropertyName("isVerified")]
	public bool? IsVerified { get; set; }

	[JsonPropertyName("isChatOwner")]
	public bool? IsChatOwner { get; set; }

	[JsonPropertyName("isChatSponsor")]
	public bool? IsChatSponsor { get; set; }

	[JsonPropertyName("isChatModerator")]
	public bool? IsChatModerator { get; set; }
}

internal sealed class YouTubeIdWire
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("status")]
	public YouTubeBroadcastStatusWire? Status { get; set; }
}

internal sealed record YouTubeChatMessageInsertWire(
	[property: JsonPropertyName("snippet")] YouTubeChatMessageInsertSnippetWire Snippet);

internal sealed record YouTubeChatMessageInsertSnippetWire(
	[property: JsonPropertyName("liveChatId")] string LiveChatId,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("textMessageDetails")] YouTubeTextMessageInsertWire TextMessageDetails);

internal sealed record YouTubeTextMessageInsertWire([property: JsonPropertyName("messageText")] string MessageText);

internal sealed record YouTubeBanInsertWire([property: JsonPropertyName("snippet")] YouTubeBanInsertSnippetWire Snippet);

internal sealed record YouTubeBanInsertSnippetWire(
	[property: JsonPropertyName("liveChatId")] string LiveChatId,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("banDurationSeconds")] long? BanDurationSeconds,
	[property: JsonPropertyName("bannedUserDetails")] YouTubeBannedUserInsertWire BannedUserDetails);

internal sealed record YouTubeBannedUserInsertWire([property: JsonPropertyName("channelId")] string ChannelId);

internal sealed record YouTubeVideoUpdateWire(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("snippet")] YouTubeVideoUpdateSnippetWire Snippet);

internal sealed record YouTubeVideoUpdateSnippetWire(
	[property: JsonPropertyName("title")] string Title,
	[property: JsonPropertyName("categoryId")] string CategoryId,
	[property: JsonPropertyName("description")] string Description,
	[property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
	[property: JsonPropertyName("defaultLanguage")] string? DefaultLanguage,
	[property: JsonPropertyName("defaultAudioLanguage")] string? DefaultAudioLanguage);

internal sealed record YouTubeCuepointWire(
	[property: JsonPropertyName("cueType")] string CueType,
	[property: JsonPropertyName("durationSecs")] int DurationSecs);
