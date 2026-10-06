using System.Globalization;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Integrations.YouTube;

internal sealed class YouTubeEventEmitter
{
	private readonly IEventPublisher _publisher;

	public YouTubeEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void PublishStreamOnline(YouTubeAccount account, YouTubeAccountState state)
		=> Publish(account,
			YouTubeEventIds.StreamOnline,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["title"] = state.StreamTitle ?? string.Empty,
				["startedAt"] = state.StreamStartedAt?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
				["broadcastId"] = state.BroadcastId ?? string.Empty
			});

	public void PublishStreamOffline(YouTubeAccount account)
		=> Publish(account, YouTubeEventIds.StreamOffline, new Dictionary<string, object?>(StringComparer.Ordinal));

	public void PublishChatEvent(YouTubeAccount account, YouTubeChatMessage message)
	{
		if (Describe(message) is not { } described)
		{
			return;
		}

		described.Values["authorChannelId"] = message.Author.ChannelId;
		described.Values["authorName"] = message.Author.DisplayName;
		Publish(account, described.EventId, described.Values, message.DisplayMessage);
	}

	private void Publish(
		YouTubeAccount account,
		string eventId,
		Dictionary<string, object?> values,
		string? message = null)
	{
		values["account"] = account.ChannelId;
		values["accountName"] = account.Title;
		_publisher.Publish(eventId, values);

		_publisher.Publish(YouTubeEventIds.Any,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["account"] = account.ChannelId,
				["accountName"] = account.Title,
				["type"] = eventId,
				["authorChannelId"] = values.GetValueOrDefault("authorChannelId") ?? string.Empty,
				["authorName"] = values.GetValueOrDefault("authorName") ?? string.Empty,
				["message"] = message ?? string.Empty
			});
	}

	private static (string EventId, Dictionary<string, object?> Values)? Describe(YouTubeChatMessage message)
		=> message switch
		{
			{ Type: YouTubeChatMessageTypes.SuperChat, SuperChat: { } superChat } => (YouTubeEventIds.SuperChat,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["messageId"] = message.Id,
					["amount"] = superChat.AmountDisplayString,
					["amountMicros"] = superChat.AmountMicros,
					["currency"] = superChat.Currency,
					["tier"] = superChat.Tier,
					["comment"] = superChat.UserComment ?? string.Empty
				}),
			{ Type: YouTubeChatMessageTypes.SuperSticker, SuperSticker: { } sticker } => (YouTubeEventIds.SuperSticker,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["messageId"] = message.Id,
					["amount"] = sticker.AmountDisplayString,
					["amountMicros"] = sticker.AmountMicros,
					["currency"] = sticker.Currency,
					["tier"] = sticker.Tier,
					["sticker"] = sticker.AltText ?? string.Empty
				}),
			{ Type: YouTubeChatMessageTypes.NewSponsor } => (YouTubeEventIds.NewMember,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["levelName"] = message.NewSponsor?.MemberLevelName ?? string.Empty,
					["isUpgrade"] = message.NewSponsor?.IsUpgrade ?? false
				}),
			{ Type: YouTubeChatMessageTypes.MemberMilestone } => (YouTubeEventIds.MemberMilestone,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["levelName"] = message.MemberMilestone?.MemberLevelName ?? string.Empty,
					["months"] = message.MemberMilestone?.MemberMonth ?? 0,
					["comment"] = message.MemberMilestone?.UserComment ?? string.Empty
				}),
			{ Type: YouTubeChatMessageTypes.MembershipGifting } => (YouTubeEventIds.MembershipGift,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["levelName"] = message.MembershipGifting?.GiftMembershipsLevelName ?? string.Empty,
					["giftCount"] = message.MembershipGifting?.GiftMembershipsCount ?? 0
				}),
			_ => null
		};
}
