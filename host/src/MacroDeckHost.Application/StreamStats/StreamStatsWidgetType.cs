namespace MacroDeckHost.Application.StreamStats;

public static class StreamStatsWidgetType
{
	public const string LocalId = "stats";

	public const string AccountKey = "account";

	public const string StyleKey = "style";

	public const string MetricKey = "metric";

	public const string TilesKey = "tiles";

	public const string DetailsKey = "details";

	public const string ThumbnailKey = "showThumbnail";

	public const string BackgroundColorKey = "backgroundColor";

	public const string OverviewStyle = "overview";

	public const string StatsRowStyle = "statsRow";

	public const string LiveRowStyle = "liveRow";

	public const string ValueGraphStyle = "valueGraph";

	public const string ValueStyle = "value";

	public const string DefaultStyle = OverviewStyle;

	public static IReadOnlyList<string> Styles { get; } =
		[OverviewStyle, StatsRowStyle, LiveRowStyle, ValueGraphStyle, ValueStyle];

	public static string QualifiedId(string ownerId) => ownerId + "::" + LocalId;
}
