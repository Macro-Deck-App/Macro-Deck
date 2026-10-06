using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.StreamStats;

internal static class StreamStatsWidgetSample
{
	public const string SampleUptime = "02:14:26";

	public static async Task<StreamStatsViewState> BuildAsync(StreamPlatform platform, IWidgetSampleTextResolver text)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(text);

		var details = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var detail in platform.Stats.Details)
		{
			details[detail.Id] = detail switch
			{
				{ Kind: StreamStatsDetailKind.Duration } => SampleUptime,
				{ Sample: { } sample } => await text.ResolveAsync(sample()).ConfigureAwait(false),
				_ => string.Empty,
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
			Points = [0.1, 0.2, 0.15, 0.3, 0.28, 0.45, 0.4, 0.6, 0.55, 0.8, 0.75, 1],
		};
	}
}
