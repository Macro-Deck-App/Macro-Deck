using System.Collections.Frozen;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Widgets.StreamStats;

internal sealed record StreamStatsViewState
{
	public const string Unavailable = "-";

	public static readonly StreamStatsViewState Empty = new();

	public bool HasAccount { get; init; }

	public bool IsLive { get; init; }

	public string ChannelName { get; init; } = string.Empty;

	public IReadOnlyDictionary<string, string> Metrics { get; init; } = FrozenDictionary<string, string>.Empty;

	public IReadOnlyDictionary<string, string> Details { get; init; } = FrozenDictionary<string, string>.Empty;

	public IReadOnlyList<double> Points { get; init; } = [];

	public UiResource? Thumbnail { get; init; }

	public bool HasChart => Points.Count >= 2;

	public string Value(string metric) => Metrics.TryGetValue(metric, out var value) ? value : Unavailable;

	public string Detail(string? detail)
		=> detail is not null && Details.TryGetValue(detail, out var value) ? value : string.Empty;

	public bool Equals(StreamStatsViewState? other)
		=> other is not null &&
			HasAccount == other.HasAccount &&
			IsLive == other.IsLive &&
			ChannelName == other.ChannelName &&
			SameEntries(Metrics, other.Metrics) &&
			SameEntries(Details, other.Details) &&
			Equals(Thumbnail, other.Thumbnail) &&
			Points.SequenceEqual(other.Points);

	public override int GetHashCode()
		=> HashCode.Combine(HasAccount, IsLive, Metrics.Count, Details.Count, Points.Count);

	private static bool SameEntries(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
		=> left.Count == right.Count &&
			left.All(entry => right.TryGetValue(entry.Key, out var value) && value == entry.Value);
}
