namespace MacroDeckHost.Application.AdGuardHome;

public static class AdGuardHomeWidgetType
{
	public const string OwnerId = "app.macro-deck.adguard-home";

	public const string LocalId = "adguard-home";

	public const string QualifiedId = OwnerId + "::" + LocalId;

	public const string InstanceKey = "instance";

	public const string DisplayNameKey = "displayName";

	public const string ViewKey = "view";

	public const string StatisticsKey = "statistics";

	public const string DurationsKey = "durations";

	public const string ShowVersionKey = "showVersion";

	public const string ShowStatusKey = "showStatus";

	public const string BackgroundColorKey = "backgroundColor";

	public const string ControlView = "control";

	public const string StatisticsView = "statistics";

	public const string OverviewView = "overview";

	public static IReadOnlyList<string> Views { get; } = [ControlView, StatisticsView, OverviewView];

	public const string DnsQueriesStatistic = "dnsQueries";

	public const string BlockedStatistic = "blocked";

	public const string BlockedPercentageStatistic = "blockedPercentage";

	public const string AverageProcessingTimeStatistic = "averageProcessingTime";

	public const string SafeBrowsingStatistic = "safeBrowsing";

	public const string ParentalStatistic = "parental";

	public const string SafeSearchStatistic = "safeSearch";

	public static IReadOnlyList<string> Statistics { get; } =
	[
		DnsQueriesStatistic,
		BlockedStatistic,
		BlockedPercentageStatistic,
		AverageProcessingTimeStatistic,
		SafeBrowsingStatistic,
		ParentalStatistic,
		SafeSearchStatistic,
	];

	public static IReadOnlyList<string> DefaultStatistics { get; } =
		[DnsQueriesStatistic, BlockedStatistic, BlockedPercentageStatistic, AverageProcessingTimeStatistic];

	public const string IndefiniteDuration = "indefinite";

	public static IReadOnlyList<AdGuardHomePauseDuration> PauseDurations { get; } =
	[
		new("5m", TimeSpan.FromMinutes(5)),
		new("15m", TimeSpan.FromMinutes(15)),
		new("30m", TimeSpan.FromMinutes(30)),
		new("1h", TimeSpan.FromHours(1)),
		new("2h", TimeSpan.FromHours(2)),
		new("8h", TimeSpan.FromHours(8)),
		new("24h", TimeSpan.FromHours(24)),
		new(IndefiniteDuration, null),
	];

	public static IReadOnlyList<string> DurationIds { get; } = [.. PauseDurations.Select(duration => duration.Id)];

	public static IReadOnlyList<string> DefaultDurations { get; } = ["5m", "1h", "8h", IndefiniteDuration];

	public static AdGuardHomePauseDuration? Duration(string? id)
		=> PauseDurations.FirstOrDefault(duration => string.Equals(duration.Id, id, StringComparison.Ordinal));
}

public sealed record AdGuardHomePauseDuration(string Id, TimeSpan? Length);
