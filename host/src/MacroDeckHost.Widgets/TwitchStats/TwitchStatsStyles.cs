using MacroDeckHost.Application.Twitch.Stats;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsStyles
{
	public const string Overview = TwitchStatsWidgetType.DefaultStyle;

	public const string StatsRow = "statsRow";

	public const string LiveRow = "liveRow";

	public const string ValueGraph = "valueGraph";

	public const string Value = "value";

	public static IReadOnlyList<string> All { get; } = [Overview, StatsRow, LiveRow, ValueGraph, Value];

	public static IReadOnlyList<string> WithMetric { get; } = [LiveRow, ValueGraph, Value];

	public static IReadOnlyList<string> WithTiles { get; } = [Overview, StatsRow];

	public static IReadOnlyList<string> WithDetails { get; } = [Overview];

	public static string Normalize(string? style)
		=> style is not null && All.Contains(style, StringComparer.Ordinal) ? style : Overview;
}

internal static class TwitchStatsMetrics
{
	public const string Viewers = TwitchStatsWidgetType.DefaultMetric;

	public const string Chatters = "chatters";

	public const string Followers = "followers";

	public const string Subscribers = "subscribers";

	public static IReadOnlyList<string> All { get; } = [Viewers, Chatters, Followers, Subscribers];

	public static IReadOnlyList<string> DefaultTiles { get; } = [Viewers, Chatters, Followers];

	public static string Normalize(string? metric)
		=> metric is not null && All.Contains(metric, StringComparer.Ordinal) ? metric : Viewers;

	public static string VariableName(string metric)
		=> metric switch
		{
			Chatters => "chatter_count",
			Followers => "follower_count",
			Subscribers => "subscriber_count",
			_ => "viewer_count",
		};

	public static bool OnlyWhileLive(string metric) => metric is Viewers or Chatters;
}

internal static class TwitchStatsDetails
{
	public const string Title = "title";

	public const string Category = "category";

	public const string Uptime = "uptime";

	public static IReadOnlyList<string> All { get; } = [Title, Category, Uptime];
}
