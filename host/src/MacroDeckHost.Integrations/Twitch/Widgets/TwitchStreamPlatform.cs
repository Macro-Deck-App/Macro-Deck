using MacroDeck.Ui.Components;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Localization;
using TwitchStrings = MacroDeckHost.Localization.AppStrings.Integrations.Twitch;

namespace MacroDeckHost.Integrations.Twitch.Widgets;

internal static class TwitchStreamPlatform
{
	public static StreamPlatform Platform { get; } = new()
	{
		OwnerId = TwitchIntegration.IntegrationId,
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
			Title = account => AppStrings.Integrations.StreamChat.Widget.Title(account),
			SharedChat = channel => TwitchStrings.ChatDialog.SharedChat(channel),
			AccountChanged = TwitchStrings.ChatDialog.AccountChanged,
			AccountUnavailable = TwitchStrings.ChatDialog.AccountUnavailable,
			NotPermitted = TwitchStrings.ChatDialog.NotPermitted,
			Refused = TwitchStrings.ChatDialog.Refused,
			MissingScope = TwitchStrings.Errors.MissingScopePermission
		},
		Stats = new StreamStatsDescriptor
		{
			Name = TwitchStrings.StatsWidget.Name,
			Description = TwitchStrings.StatsWidget.Description,
			Heading = TwitchStrings.StatsWidget.Heading,
			AccountDescription = AppStrings.Integrations.StreamStats.Widget.AccountDescription,
			Metrics =
			[
				new StreamStatsMetric("viewers",
					"viewer_count",
					AppStrings.Integrations.StreamStats.Widget.Viewers,
					UiIcons.User,
					true,
					"1234"),
				new StreamStatsMetric("chatters",
					"chatter_count",
					TwitchStrings.StatsWidget.Chatters,
					UiIcons.MessageSquare,
					true,
					"256"),
				new StreamStatsMetric("followers",
					"follower_count",
					TwitchStrings.StatsWidget.Followers,
					UiIcons.Heart,
					false,
					"12400"),
				new StreamStatsMetric("subscribers",
					"subscriber_count",
					AppStrings.Integrations.StreamStats.Widget.Subscribers,
					UiIcons.Star,
					false,
					"842")
			],
			DefaultMetric = "viewers",
			DefaultTiles = ["viewers", "chatters", "followers"],
			Details =
			[
				new StreamStatsDetail("title",
					"stream_title",
					TwitchStrings.Variables.StreamTitle,
					StreamStatsDetailKind.Title,
					AppStrings.Integrations.StreamStats.Widget.SampleTitle),
				new StreamStatsDetail("category",
					"stream_category",
					TwitchStrings.Variables.StreamCategory,
					StreamStatsDetailKind.Text,
					TwitchStrings.StatsWidget.SampleCategory),
				new StreamStatsDetail("uptime",
					"uptime_seconds",
					AppStrings.Integrations.StreamStats.Widget.Uptime,
					StreamStatsDetailKind.Duration)
			],
			LiveRowDetail = "category"
		},
		Thumbnails = new StreamThumbnailRule("static-cdn.jtvnw.net",
			"/previews-ttv/",
			accountId => accountId.All(char.IsAsciiDigit))
	};
}
