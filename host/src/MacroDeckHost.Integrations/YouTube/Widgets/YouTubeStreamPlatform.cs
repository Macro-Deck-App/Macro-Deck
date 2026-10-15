using MacroDeck.Ui.Components;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Localization;
using YouTubeStrings = MacroDeckHost.Localization.AppStrings.Integrations.YouTube;

namespace MacroDeckHost.Integrations.YouTube.Widgets;

internal static class YouTubeStreamPlatform
{
	public static StreamPlatform Platform { get; } = new()
	{
		OwnerId = YouTubeIntegration.IntegrationId,
		DialogViewId = "youtube-chat",
		AccentColor = "#ff0000",
		AccountLabel = YouTubeStrings.Actions.AccountLabel,
		Chat = new StreamChatTexts
		{
			Name = YouTubeStrings.ChatWidget.Name,
			Description = YouTubeStrings.ChatWidget.Description,
			Heading = YouTubeStrings.ChatWidget.Name,
			AccountDescription = AppStrings.Integrations.StreamChat.Widget.AccountDescription,
			Offline = YouTubeStrings.ChatWidget.Offline,
			Title = account => AppStrings.Integrations.StreamChat.Widget.Title(account),
			AccountChanged = YouTubeStrings.ChatDialog.AccountChanged,
			AccountUnavailable = YouTubeStrings.ChatDialog.AccountUnavailable,
			NotPermitted = YouTubeStrings.ChatDialog.NotPermitted,
			Refused = YouTubeStrings.ChatDialog.Refused,
			MissingScope = YouTubeStrings.Errors.MissingPermission,
			Unsupported = YouTubeStrings.ChatDialog.Unsupported
		},
		Stats = new StreamStatsDescriptor
		{
			Name = YouTubeStrings.StatsWidget.Name,
			Description = YouTubeStrings.StatsWidget.Description,
			Heading = YouTubeStrings.StatsWidget.Heading,
			AccountDescription = AppStrings.Integrations.StreamStats.Widget.AccountDescription,
			Metrics =
			[
				new StreamStatsMetric("viewers",
					"viewer_count",
					AppStrings.Integrations.StreamStats.Widget.Viewers,
					UiIcons.User,
					true,
					"1234"),
				new StreamStatsMetric("likes",
					"like_count",
					YouTubeStrings.StatsWidget.Likes,
					UiIcons.Heart,
					true,
					"318"),
				new StreamStatsMetric("subscribers",
					"subscriber_count",
					AppStrings.Integrations.StreamStats.Widget.Subscribers,
					UiIcons.Star,
					false,
					"12400")
			],
			DefaultMetric = "viewers",
			DefaultTiles = ["viewers", "likes", "subscribers"],
			Details =
			[
				new StreamStatsDetail("title",
					"stream_title",
					YouTubeStrings.Variables.StreamTitle,
					StreamStatsDetailKind.Title,
					AppStrings.Integrations.StreamStats.Widget.SampleTitle),
				new StreamStatsDetail("uptime",
					"uptime_seconds",
					AppStrings.Integrations.StreamStats.Widget.Uptime,
					StreamStatsDetailKind.Duration)
			],
			LiveRowDetail = "title"
		},
		Thumbnails = new StreamThumbnailRule("i.ytimg.com", "/vi/", IsYouTubeChannelId)
	};

	private static bool IsYouTubeChannelId(string accountId)
	{
		return accountId.Length > 0
			&& accountId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
	}
}
