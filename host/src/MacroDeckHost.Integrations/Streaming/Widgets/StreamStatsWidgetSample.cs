using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal static class StreamStatsWidgetSample
{
	public const string SampleUptime = "02:14:26";

	public static StreamStatsViewState Build(StreamPlatform platform, string? culture)
	{
		ArgumentNullException.ThrowIfNull(platform);

		var details = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var detail in platform.Stats.Details)
		{
			details[detail.Id] = detail switch
			{
				{ Kind: StreamStatsDetailKind.Duration } => SampleUptime,
				{ Sample: { } sample } => WidgetText.Resolve(sample(), culture),
				_ => string.Empty
			};
		}

		return new StreamStatsViewState
		{
			HasAccount = true,
			IsLive = true,
			Metrics = platform.Stats.Metrics.ToDictionary(metric => metric.Id,
				metric => StreamStatsResolver.Count(metric.SampleValue),
				StringComparer.Ordinal),
			Details = details,
			Points = [0.1, 0.2, 0.15, 0.3, 0.28, 0.45, 0.4, 0.6, 0.55, 0.8, 0.75, 1]
		};
	}
}
