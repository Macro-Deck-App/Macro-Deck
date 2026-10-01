using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Widgets.TwitchStats;

internal sealed record TwitchStatsViewState
{
	public const string Unavailable = "-";

	public static readonly TwitchStatsViewState Empty = new();

	public bool HasAccount { get; init; }

	public bool IsLive { get; init; }

	public string ChannelName { get; init; } = string.Empty;

	public string Viewers { get; init; } = Unavailable;

	public string Chatters { get; init; } = Unavailable;

	public string Followers { get; init; } = Unavailable;

	public string Subscribers { get; init; } = Unavailable;

	public string Title { get; init; } = string.Empty;

	public string Category { get; init; } = string.Empty;

	public string Uptime { get; init; } = string.Empty;

	public IReadOnlyList<double> Points { get; init; } = [];

	public UiResource? Thumbnail { get; init; }

	public bool HasChart => Points.Count >= 2;

	public string Value(string metric)
		=> metric switch
		{
			TwitchStatsMetrics.Chatters => Chatters,
			TwitchStatsMetrics.Followers => Followers,
			TwitchStatsMetrics.Subscribers => Subscribers,
			_ => Viewers,
		};

	public bool Equals(TwitchStatsViewState? other)
		=> other is not null &&
			HasAccount == other.HasAccount &&
			IsLive == other.IsLive &&
			ChannelName == other.ChannelName &&
			Viewers == other.Viewers &&
			Chatters == other.Chatters &&
			Followers == other.Followers &&
			Subscribers == other.Subscribers &&
			Title == other.Title &&
			Category == other.Category &&
			Uptime == other.Uptime &&
			Equals(Thumbnail, other.Thumbnail) &&
			Points.SequenceEqual(other.Points);

	public override int GetHashCode()
		=> HashCode.Combine(HasAccount, IsLive, Viewers, Chatters, Followers, Subscribers, Points.Count);
}
