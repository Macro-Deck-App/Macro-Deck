namespace MacroDeckHost.Integrations.Twitch.Auth;

internal static class TwitchScopes
{
	public const string UserReadChat = "user:read:chat";
	public const string UserWriteChat = "user:write:chat";
	public const string ChannelReadSubscriptions = "channel:read:subscriptions";
	public const string ModeratorReadFollowers = "moderator:read:followers";
	public const string BitsRead = "bits:read";
	public const string ChannelReadAds = "channel:read:ads";
	public const string ChannelReadHypeTrain = "channel:read:hype_train";
	public const string ChannelReadGoals = "channel:read:goals";
	public const string ChannelModerate = "channel:moderate";
	public const string ModeratorReadShoutouts = "moderator:read:shoutouts";
	public const string ChannelManageBroadcast = "channel:manage:broadcast";
	public const string ChannelManagePolls = "channel:manage:polls";
	public const string ChannelManagePredictions = "channel:manage:predictions";
	public const string ChannelManageRedemptions = "channel:manage:redemptions";
	public const string ChannelManageVips = "channel:manage:vips";
	public const string ChannelManageRaids = "channel:manage:raids";
	public const string ChannelEditCommercial = "channel:edit:commercial";
	public const string ChannelManageAds = "channel:manage:ads";
	public const string ClipsEdit = "clips:edit";
	public const string ModeratorManageAnnouncements = "moderator:manage:announcements";
	public const string ModeratorManageShoutouts = "moderator:manage:shoutouts";
	public const string ModeratorManageChatMessages = "moderator:manage:chat_messages";
	public const string ModeratorManageChatSettings = "moderator:manage:chat_settings";
	public const string ModeratorManageBannedUsers = "moderator:manage:banned_users";

	public static IReadOnlyList<string> All { get; } =
	[
		UserReadChat,
		UserWriteChat,
		ChannelReadSubscriptions,
		ModeratorReadFollowers,
		BitsRead,
		ChannelReadAds,
		ChannelReadHypeTrain,
		ChannelReadGoals,
		ChannelModerate,
		ModeratorReadShoutouts,
		ChannelManageBroadcast,
		ChannelManagePolls,
		ChannelManagePredictions,
		ChannelManageRedemptions,
		ChannelManageVips,
		ChannelManageRaids,
		ChannelEditCommercial,
		ChannelManageAds,
		ClipsEdit,
		ModeratorManageAnnouncements,
		ModeratorManageShoutouts,
		ModeratorManageChatMessages,
		ModeratorManageChatSettings,
		ModeratorManageBannedUsers
	];

	public static string Requested { get; } = string.Join(' ', All);
}
