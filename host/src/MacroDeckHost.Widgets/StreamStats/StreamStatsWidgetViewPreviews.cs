using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Widgets.StreamStats;

internal static class StreamStatsWidgetViewPreviews
{
	private const string Viewers = "viewers";
	private const string Chatters = "chatters";
	private const string Followers = "followers";
	private const string Subscribers = "subscribers";

	private static readonly StreamPlatform _platform = StreamPlatforms.Twitch;

	private static readonly StreamStatsViewState _live = new()
	{
		HasAccount = true,
		IsLive = true,
		Metrics = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[Viewers] = "1,234",
			[Chatters] = "256",
			[Followers] = "12.4K",
			[Subscribers] = "842",
		},
		Details = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["title"] = "Exploring Night City",
			["category"] = "Cyberpunk 2077",
			["uptime"] = "02:14:26",
		},
		Points = [0.1, 0.2, 0.15, 0.3, 0.28, 0.45, 0.4, 0.6, 0.55, 0.8, 0.75, 1],
	};

	private static readonly StreamStatsViewState _offline = new()
	{
		HasAccount = true,
		Metrics = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[Followers] = "12.4K",
			[Subscribers] = "842",
		},
	};

	[UiPreview("Overview (3x2)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Overview() => Build(_live, StreamStatsStyles.Overview);

	[UiPreview("Overview, four tiles, no thumbnail", Profile = UiPreviewProfiles.Widget)]
	public static UiElement OverviewCustomised()
		=> Build(_live, StreamStatsWidgetOptions.Default(_platform) with
		{
			Tiles = [Subscribers, Viewers, Chatters, Followers],
			Details = ["category", "title"],
			ShowThumbnail = false,
		});

	[UiPreview("Overview offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement OverviewOffline() => Build(_offline, StreamStatsStyles.Overview);

	[UiPreview("Stats row (3x1)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StatsRow() => Build(_live, StreamStatsStyles.StatsRow);

	[UiPreview("Stats row, four tiles", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StatsRowFour()
		=> Build(_live,
			StreamStatsWidgetOptions.Default(_platform) with
			{
				Style = StreamStatsStyles.StatsRow, Tiles = _platform.Stats.MetricIds,
			});

	[UiPreview("Stats row offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StatsRowOffline() => Build(_offline, StreamStatsStyles.StatsRow);

	[UiPreview("Live status row (3x1)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LiveRow() => Build(_live, StreamStatsStyles.LiveRow);

	[UiPreview("Live status row offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LiveRowOffline() => Build(_offline, StreamStatsStyles.LiveRow);

	[UiPreview("Value with graph (2x2)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueGraph() => Build(_live, StreamStatsStyles.ValueGraph);

	[UiPreview("Value with graph, followers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueGraphFollowers()
		=> Build(_live, StreamStatsStyles.ValueGraph, Followers);

	[UiPreview("Value with graph offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueGraphOffline() => Build(_offline, StreamStatsStyles.ValueGraph);

	[UiPreview("Value (1x1), viewers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueViewers() => Build(_live, StreamStatsStyles.Value);

	[UiPreview("Value (1x1), chatters", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueChatters() => Build(_live, StreamStatsStyles.Value, Chatters);

	[UiPreview("Value (1x1), followers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueFollowers() => Build(_live, StreamStatsStyles.Value, Followers);

	[UiPreview("Value (1x1), subscribers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueSubscribers()
		=> Build(_live, StreamStatsStyles.Value, Subscribers);

	[UiPreview("Value (1x1) offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueOffline() => Build(_offline, StreamStatsStyles.Value);

	private static UiElement Build(StreamStatsViewState state, string style, string metric = Viewers)
		=> Build(state, StreamStatsWidgetOptions.Default(_platform) with { Style = style, Metric = metric });

	private static UiElement Build(StreamStatsViewState state, StreamStatsWidgetOptions options)
		=> StreamStatsWidgetView.Build(_platform,
			new UiState<StreamStatsViewState>(state),
			options,
			logo: StreamStatsLogo.Register(_platform, WidgetPreviewResources.Integrations,
				WidgetPreviewResources.Store));
}
