using MacroDeck.Localization;

namespace MacroDeckHost.Application.StreamStats;

public sealed record StreamStatsMetric(
	string Id,
	string VariableName,
	Func<LocalizedString> Label,
	string Icon,
	bool OnlyWhileLive,
	string SampleValue);

public enum StreamStatsDetailKind
{
	Title,

	Text,

	Duration
}

public sealed record StreamStatsDetail(
	string Id,
	string VariableName,
	Func<LocalizedString> Label,
	StreamStatsDetailKind Kind,
	Func<LocalizedString>? Sample = null);

public sealed record StreamThumbnailRule(
	string AllowedHost,
	string AllowedPathPrefix,
	Func<string, bool> IsValidAccountId);

public sealed class StreamStatsDescriptor
{
	public required Func<LocalizedString> Name { get; init; }

	public required Func<LocalizedString> Description { get; init; }

	public required Func<LocalizedString> Heading { get; init; }

	public required Func<LocalizedString> AccountDescription { get; init; }

	public required IReadOnlyList<StreamStatsMetric> Metrics { get; init; }

	public required string DefaultMetric { get; init; }

	public required IReadOnlyList<string> DefaultTiles { get; init; }

	public required IReadOnlyList<StreamStatsDetail> Details { get; init; }

	public string? LiveRowDetail { get; init; }

	public IReadOnlyList<string> MetricIds => [.. Metrics.Select(metric => metric.Id)];

	public IReadOnlyList<string> DetailIds => [.. Details.Select(detail => detail.Id)];

	public StreamStatsMetric? Metric(string? id)
		=> id is null ? null : Metrics.FirstOrDefault(metric => string.Equals(metric.Id, id, StringComparison.Ordinal));

	public StreamStatsDetail? Detail(string? id)
		=> id is null ? null : Details.FirstOrDefault(detail => string.Equals(detail.Id, id, StringComparison.Ordinal));

	public string NormalizeMetric(string? id) => Metric(id)?.Id ?? DefaultMetric;
}
