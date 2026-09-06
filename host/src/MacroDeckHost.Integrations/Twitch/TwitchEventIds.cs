namespace MacroDeckHost.Integrations.Twitch;

internal static class TwitchEventIds
{
	public const string StreamOnline = "stream-online";
	public const string StreamOffline = "stream-offline";
	public const string ChannelUpdate = "channel-update";
	public const string AdBreakBegin = "ad-break-begin";

	public const string Follow = "follow";
	public const string RaidIncoming = "raid-incoming";
	public const string RaidOutgoing = "raid-outgoing";
	public const string ShoutoutCreated = "shoutout-created";
	public const string ShoutoutReceived = "shoutout-received";
	public const string VipAdded = "vip-added";
	public const string VipRemoved = "vip-removed";

	public const string Subscribe = "subscribe";
	public const string SubscriptionMessage = "subscription-message";
	public const string SubscriptionGift = "subscription-gift";
	public const string SubscriptionEnd = "subscription-end";

	public const string Cheer = "cheer";

	public const string RewardRedeemed = "reward-redeemed";
	public const string RewardRedemptionUpdated = "reward-redemption-updated";
	public const string AutomaticRewardRedeemed = "automatic-reward-redeemed";

	public const string PollBegin = "poll-begin";
	public const string PollEnd = "poll-end";
	public const string PredictionBegin = "prediction-begin";
	public const string PredictionLock = "prediction-lock";
	public const string PredictionEnd = "prediction-end";

	public const string HypeTrainBegin = "hype-train-begin";
	public const string HypeTrainProgress = "hype-train-progress";
	public const string HypeTrainEnd = "hype-train-end";

	public const string GoalBegin = "goal-begin";
	public const string GoalProgress = "goal-progress";
	public const string GoalEnd = "goal-end";

	public const string ChatNotification = "chat-notification";
	public const string ChatCleared = "chat-cleared";
	public const string ChatMessageDeleted = "chat-message-deleted";
	public const string ChatSettingsUpdated = "chat-settings-updated";

	public const string Ban = "ban";
	public const string Unban = "unban";

	public const string Connected = "connected";
	public const string Disconnected = "disconnected";

	public const string Any = "event";
}
