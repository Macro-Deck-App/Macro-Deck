using MacroDeckHost.Application.StreamStats;

namespace MacroDeckHost.Widgets.StreamStats;

internal static class StreamStatsStyles
{
	public const string Overview = StreamStatsWidgetType.OverviewStyle;

	public const string StatsRow = StreamStatsWidgetType.StatsRowStyle;

	public const string LiveRow = StreamStatsWidgetType.LiveRowStyle;

	public const string ValueGraph = StreamStatsWidgetType.ValueGraphStyle;

	public const string Value = StreamStatsWidgetType.ValueStyle;

	public static IReadOnlyList<string> All => StreamStatsWidgetType.Styles;

	public static IReadOnlyList<string> WithMetric { get; } = [LiveRow, ValueGraph, Value];

	public static IReadOnlyList<string> WithTiles { get; } = [Overview, StatsRow];

	public static IReadOnlyList<string> WithDetails { get; } = [Overview];

	public static string Normalize(string? style)
		=> style is not null && All.Contains(style, StringComparer.Ordinal) ? style : Overview;
}
