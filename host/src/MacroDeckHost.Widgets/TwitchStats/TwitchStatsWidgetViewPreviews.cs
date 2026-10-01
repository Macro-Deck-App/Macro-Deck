using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsWidgetViewPreviews
{
	private static readonly TwitchStatsViewState _live = new()
	{
		HasAccount = true,
		IsLive = true,
		Viewers = "1,234",
		Chatters = "256",
		Followers = "12.4K",
		Subscribers = "842",
		Title = "Exploring Night City",
		Category = "Cyberpunk 2077",
		Uptime = "02:14:26",
		Points = [0.1, 0.2, 0.15, 0.3, 0.28, 0.45, 0.4, 0.6, 0.55, 0.8, 0.75, 1],
	};

	private static readonly TwitchStatsViewState _offline = new()
	{
		HasAccount = true,
		Followers = "12.4K",
		Subscribers = "842",
	};

	[UiPreview("Overview (3x2)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Overview() => Build(_live, TwitchStatsStyles.Overview);

	[UiPreview("Overview, four tiles, no thumbnail", Profile = UiPreviewProfiles.Widget)]
	public static UiElement OverviewCustomised()
		=> Build(_live, new TwitchStatsWidgetOptions
		{
			Tiles = [TwitchStatsMetrics.Subscribers, TwitchStatsMetrics.Viewers, TwitchStatsMetrics.Chatters,
				TwitchStatsMetrics.Followers],
			Details = [TwitchStatsDetails.Category, TwitchStatsDetails.Title],
			ShowThumbnail = false,
		});

	[UiPreview("Overview offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement OverviewOffline() => Build(_offline, TwitchStatsStyles.Overview);

	[UiPreview("Stats row (3x1)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StatsRow() => Build(_live, TwitchStatsStyles.StatsRow);

	[UiPreview("Stats row, four tiles", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StatsRowFour()
		=> Build(_live, new TwitchStatsWidgetOptions { Style = TwitchStatsStyles.StatsRow, Tiles = TwitchStatsMetrics.All });

	[UiPreview("Stats row offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StatsRowOffline() => Build(_offline, TwitchStatsStyles.StatsRow);

	[UiPreview("Live status row (3x1)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LiveRow() => Build(_live, TwitchStatsStyles.LiveRow);

	[UiPreview("Live status row offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LiveRowOffline() => Build(_offline, TwitchStatsStyles.LiveRow);

	[UiPreview("Value with graph (2x2)", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueGraph() => Build(_live, TwitchStatsStyles.ValueGraph);

	[UiPreview("Value with graph, followers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueGraphFollowers()
		=> Build(_live, TwitchStatsStyles.ValueGraph, TwitchStatsMetrics.Followers);

	[UiPreview("Value with graph offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueGraphOffline() => Build(_offline, TwitchStatsStyles.ValueGraph);

	[UiPreview("Value (1x1), viewers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueViewers() => Build(_live, TwitchStatsStyles.Value);

	[UiPreview("Value (1x1), chatters", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueChatters() => Build(_live, TwitchStatsStyles.Value, TwitchStatsMetrics.Chatters);

	[UiPreview("Value (1x1), followers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueFollowers() => Build(_live, TwitchStatsStyles.Value, TwitchStatsMetrics.Followers);

	[UiPreview("Value (1x1), subscribers", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueSubscribers()
		=> Build(_live, TwitchStatsStyles.Value, TwitchStatsMetrics.Subscribers);

	[UiPreview("Value (1x1) offline", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ValueOffline() => Build(_offline, TwitchStatsStyles.Value);

	private static UiElement Build(TwitchStatsViewState state, string style, string metric = TwitchStatsMetrics.Viewers)
		=> Build(state, new TwitchStatsWidgetOptions { Style = style, Metric = metric });

	private static UiElement Build(TwitchStatsViewState state, TwitchStatsWidgetOptions options)
		=> TwitchStatsWidgetView.Build(new UiState<TwitchStatsViewState>(state),
			options,
			logo: TwitchStatsLogo.Register(WidgetPreviewResources.Integrations, WidgetPreviewResources.Store));
}
