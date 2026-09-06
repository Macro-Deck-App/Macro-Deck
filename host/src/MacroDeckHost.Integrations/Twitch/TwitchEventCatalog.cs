using MacroDeckHost.Integrations.Twitch.Auth;

namespace MacroDeckHost.Integrations.Twitch;

internal enum TwitchConditionKind
{
	Broadcaster,

	BroadcasterAndModerator,

	BroadcasterAndUser,

	RaidIncoming,

	RaidOutgoing
}

internal sealed record TwitchSubscriptionSpec(
	string EventId,
	string Type,
	string Version,
	TwitchConditionKind Condition,
	string? Scope);

internal static class TwitchEventCatalog
{
	public static IReadOnlyList<TwitchSubscriptionSpec> All { get; } =
	[
		new(TwitchEventIds.StreamOnline, "stream.online", "1", TwitchConditionKind.Broadcaster, null),
		new(TwitchEventIds.StreamOffline, "stream.offline", "1", TwitchConditionKind.Broadcaster, null),
		new(TwitchEventIds.ChannelUpdate, "channel.update", "2", TwitchConditionKind.Broadcaster, null),
		new(TwitchEventIds.AdBreakBegin,
			"channel.ad_break.begin",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadAds),

		new(TwitchEventIds.Follow,
			"channel.follow",
			"2",
			TwitchConditionKind.BroadcasterAndModerator,
			TwitchScopes.ModeratorReadFollowers),
		new(TwitchEventIds.RaidIncoming, "channel.raid", "1", TwitchConditionKind.RaidIncoming, null),
		new(TwitchEventIds.RaidOutgoing, "channel.raid", "1", TwitchConditionKind.RaidOutgoing, null),
		new(TwitchEventIds.ShoutoutCreated,
			"channel.shoutout.create",
			"1",
			TwitchConditionKind.BroadcasterAndModerator,
			TwitchScopes.ModeratorReadShoutouts),
		new(TwitchEventIds.ShoutoutReceived,
			"channel.shoutout.receive",
			"1",
			TwitchConditionKind.BroadcasterAndModerator,
			TwitchScopes.ModeratorReadShoutouts),
		new(TwitchEventIds.VipAdded,
			"channel.vip.add",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManageVips),
		new(TwitchEventIds.VipRemoved,
			"channel.vip.remove",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManageVips),

		new(TwitchEventIds.Subscribe,
			"channel.subscribe",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadSubscriptions),
		new(TwitchEventIds.SubscriptionMessage,
			"channel.subscription.message",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadSubscriptions),
		new(TwitchEventIds.SubscriptionGift,
			"channel.subscription.gift",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadSubscriptions),
		new(TwitchEventIds.SubscriptionEnd,
			"channel.subscription.end",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadSubscriptions),

		new(TwitchEventIds.Cheer, "channel.cheer", "1", TwitchConditionKind.Broadcaster, TwitchScopes.BitsRead),

		new(TwitchEventIds.RewardRedeemed,
			"channel.channel_points_custom_reward_redemption.add",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManageRedemptions),
		new(TwitchEventIds.RewardRedemptionUpdated,
			"channel.channel_points_custom_reward_redemption.update",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManageRedemptions),
		new(TwitchEventIds.AutomaticRewardRedeemed,
			"channel.channel_points_automatic_reward_redemption.add",
			"2",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManageRedemptions),

		new(TwitchEventIds.PollBegin,
			"channel.poll.begin",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManagePolls),
		new(TwitchEventIds.PollEnd,
			"channel.poll.end",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManagePolls),
		new(TwitchEventIds.PredictionBegin,
			"channel.prediction.begin",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManagePredictions),
		new(TwitchEventIds.PredictionLock,
			"channel.prediction.lock",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManagePredictions),
		new(TwitchEventIds.PredictionEnd,
			"channel.prediction.end",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelManagePredictions),

		new(TwitchEventIds.HypeTrainBegin,
			"channel.hype_train.begin",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadHypeTrain),
		new(TwitchEventIds.HypeTrainProgress,
			"channel.hype_train.progress",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadHypeTrain),
		new(TwitchEventIds.HypeTrainEnd,
			"channel.hype_train.end",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadHypeTrain),

		new(TwitchEventIds.GoalBegin,
			"channel.goal.begin",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadGoals),
		new(TwitchEventIds.GoalProgress,
			"channel.goal.progress",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadGoals),
		new(TwitchEventIds.GoalEnd,
			"channel.goal.end",
			"1",
			TwitchConditionKind.Broadcaster,
			TwitchScopes.ChannelReadGoals),

		new(TwitchEventIds.ChatNotification,
			"channel.chat.notification",
			"1",
			TwitchConditionKind.BroadcasterAndUser,
			TwitchScopes.UserReadChat),
		new(TwitchEventIds.ChatCleared,
			"channel.chat.clear",
			"1",
			TwitchConditionKind.BroadcasterAndUser,
			TwitchScopes.UserReadChat),
		new(TwitchEventIds.ChatMessageDeleted,
			"channel.chat.message_delete",
			"1",
			TwitchConditionKind.BroadcasterAndUser,
			TwitchScopes.UserReadChat),
		new(TwitchEventIds.ChatSettingsUpdated,
			"channel.chat_settings.update",
			"1",
			TwitchConditionKind.BroadcasterAndUser,
			TwitchScopes.UserReadChat),

		new(TwitchEventIds.Ban, "channel.ban", "1", TwitchConditionKind.Broadcaster, TwitchScopes.ChannelModerate),
		new(TwitchEventIds.Unban, "channel.unban", "1", TwitchConditionKind.Broadcaster, TwitchScopes.ChannelModerate)
	];

	public static TwitchSubscriptionSpec? ForEvent(string eventId)
		=> All.FirstOrDefault(spec => string.Equals(spec.EventId, eventId, StringComparison.Ordinal));

	public static TwitchSubscriptionSpec? ForType(string? subscriptionType, string? version)
	{
		if (string.IsNullOrEmpty(subscriptionType))
		{
			return null;
		}

		var matches = All.Where(spec => string.Equals(spec.Type, subscriptionType, StringComparison.Ordinal)).ToList();

		return matches.Count switch
		{
			0 => null,
			1 => matches[0],

			_ => null
		};
	}

	public static IReadOnlyList<string> Types { get; } =
	[
		.. All.Select(spec => spec.Type).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal)
	];

	public static IReadOnlyDictionary<string, string> BuildCondition(TwitchConditionKind kind, string userId)
		=> kind switch
		{
			TwitchConditionKind.Broadcaster => new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["broadcaster_user_id"] = userId
			},
			TwitchConditionKind.BroadcasterAndModerator => new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["broadcaster_user_id"] = userId,
				["moderator_user_id"] = userId
			},
			TwitchConditionKind.BroadcasterAndUser => new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["broadcaster_user_id"] = userId,
				["user_id"] = userId
			},
			TwitchConditionKind.RaidIncoming => new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["to_broadcaster_user_id"] = userId
			},
			TwitchConditionKind.RaidOutgoing => new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["from_broadcaster_user_id"] = userId
			},
			_ => new Dictionary<string, string>(StringComparer.Ordinal) { ["broadcaster_user_id"] = userId }
		};
}
