namespace MacroDeckHost.Integrations.YouTube;

internal static class YouTubeEventIds
{
	public const string StreamOnline = "stream-online";
	public const string StreamOffline = "stream-offline";

	public const string SuperChat = "super-chat";
	public const string SuperSticker = "super-sticker";

	public const string NewMember = "new-member";
	public const string MemberMilestone = "member-milestone";
	public const string MembershipGift = "membership-gift";

	public const string Any = "event";

	public static IReadOnlyList<string> Specific { get; } =
		[StreamOnline, StreamOffline, SuperChat, SuperSticker, NewMember, MemberMilestone, MembershipGift];
}
