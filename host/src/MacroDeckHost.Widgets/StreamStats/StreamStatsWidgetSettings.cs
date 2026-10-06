using System.Text.Json;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.StreamStats;

internal sealed record StreamStatsWidgetOptions
{
	public string Style { get; init; } = StreamStatsStyles.Overview;

	public required string Metric { get; init; }

	public required IReadOnlyList<string> Tiles { get; init; }

	public required IReadOnlyList<string> Details { get; init; }

	public bool ShowThumbnail { get; init; } = true;

	public static StreamStatsWidgetOptions Default(StreamPlatform platform)
	{
		ArgumentNullException.ThrowIfNull(platform);

		return new StreamStatsWidgetOptions
		{
			Metric = platform.Stats.DefaultMetric,
			Tiles = platform.Stats.DefaultTiles,
			Details = platform.Stats.DetailIds,
		};
	}
}

internal static class StreamStatsWidgetSettings
{
	public static string? Account(JsonElement data)
		=> WidgetConfigJson.ReadString(data, StreamStatsWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;

	public static StreamStatsWidgetOptions Options(StreamPlatform platform, JsonElement data)
	{
		ArgumentNullException.ThrowIfNull(platform);

		var stats = platform.Stats;

		return new StreamStatsWidgetOptions
		{
			Style = StreamStatsStyles.Normalize(WidgetConfigJson.ReadString(data, StreamStatsWidgetType.StyleKey)),
			Metric = stats.NormalizeMetric(WidgetConfigJson.ReadString(data, StreamStatsWidgetType.MetricKey)),
			Tiles = ReadList(data, StreamStatsWidgetType.TilesKey, stats.MetricIds) ?? stats.DefaultTiles,
			Details = ReadList(data, StreamStatsWidgetType.DetailsKey, stats.DetailIds) ?? stats.DetailIds,
			ShowThumbnail = WidgetConfigJson.ReadBool(data, StreamStatsWidgetType.ThumbnailKey) ?? true,
		};
	}

	public static string? BackgroundColor(JsonElement data)
		=> WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, StreamStatsWidgetType.BackgroundColorKey));

	private static IReadOnlyList<string>? ReadList(JsonElement data, string name, IReadOnlyList<string> known)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty(name, out var list) ||
			list.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		return
		[
			.. list.EnumerateArray()
				.Where(item => item.ValueKind == JsonValueKind.String)
				.Select(item => item.GetString()!)
				.Where(id => known.Contains(id, StringComparer.Ordinal))
				.Distinct(StringComparer.Ordinal),
		];
	}
}
