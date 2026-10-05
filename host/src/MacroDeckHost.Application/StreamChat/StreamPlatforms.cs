using MacroDeck.Ui.Components;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Localization;
using TwitchStrings = MacroDeckHost.Localization.AppStrings.Integrations.Twitch;

namespace MacroDeckHost.Application.StreamChat;

public static class StreamPlatforms
{
	public static StreamPlatform Twitch { get; } = new()
	{
		OwnerId = "app.macro-deck.twitch",
		DialogViewId = "twitch-chat",
		AccentColor = "#9146ff",
		AccountLabel = TwitchStrings.Actions.AccountLabel,
		Chat = new StreamChatTexts
		{
			Name = TwitchStrings.ChatWidget.Name,
			Description = TwitchStrings.ChatWidget.Description,
			Heading = TwitchStrings.ChatWidget.Name,
			AccountDescription = AppStrings.Integrations.StreamChat.Widget.AccountDescription,
			Offline = TwitchStrings.ChatWidget.Offline,
			Title = account => AppStrings.Integrations.StreamChat.Widget.Title(account: account),
			SharedChat = channel => TwitchStrings.ChatDialog.SharedChat(channel: channel),
			AccountChanged = TwitchStrings.ChatDialog.AccountChanged,
			AccountUnavailable = TwitchStrings.ChatDialog.AccountUnavailable,
			NotPermitted = TwitchStrings.ChatDialog.NotPermitted,
			Refused = TwitchStrings.ChatDialog.Refused,
			MissingScope = TwitchStrings.Errors.MissingScopePermission,
		},
		Stats = new StreamStatsDescriptor
		{
			Name = TwitchStrings.StatsWidget.Name,
			Description = TwitchStrings.StatsWidget.Description,
			Heading = TwitchStrings.StatsWidget.Heading,
			AccountDescription = AppStrings.Integrations.StreamStats.Widget.AccountDescription,
			Metrics =
			[
				new StreamStatsMetric("viewers", "viewer_count", AppStrings.Integrations.StreamStats.Widget.Viewers,
					UiIcons.User, OnlyWhileLive: true, SampleValue: "1234"),
				new StreamStatsMetric("chatters", "chatter_count", TwitchStrings.StatsWidget.Chatters,
					UiIcons.MessageSquare, OnlyWhileLive: true, SampleValue: "256"),
				new StreamStatsMetric("followers", "follower_count", TwitchStrings.StatsWidget.Followers,
					UiIcons.Heart, OnlyWhileLive: false, SampleValue: "12400"),
				new StreamStatsMetric("subscribers", "subscriber_count",
					AppStrings.Integrations.StreamStats.Widget.Subscribers, UiIcons.Star, OnlyWhileLive: false,
					SampleValue: "842"),
			],
			DefaultMetric = "viewers",
			DefaultTiles = ["viewers", "chatters", "followers"],
			Details =
			[
				new StreamStatsDetail("title", "stream_title", TwitchStrings.Variables.StreamTitle,
					StreamStatsDetailKind.Title, AppStrings.Integrations.StreamStats.Widget.SampleTitle),
				new StreamStatsDetail("category", "stream_category", TwitchStrings.Variables.StreamCategory,
					StreamStatsDetailKind.Text, TwitchStrings.StatsWidget.SampleCategory),
				new StreamStatsDetail("uptime", "uptime_seconds", AppStrings.Integrations.StreamStats.Widget.Uptime,
					StreamStatsDetailKind.Duration),
			],
			LiveRowDetail = "category",
		},
		Thumbnails = new StreamThumbnailRule("static-cdn.jtvnw.net", "/previews-ttv/",
			accountId => accountId.All(char.IsAsciiDigit)),
	};

	public static IReadOnlyList<StreamPlatform> All { get; } = [Twitch];

	public static StreamPlatform? Find(string? ownerId)
		=> All.FirstOrDefault(platform => string.Equals(platform.OwnerId, ownerId, StringComparison.Ordinal));
}
