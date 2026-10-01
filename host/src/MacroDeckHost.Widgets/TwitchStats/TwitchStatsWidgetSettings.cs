using System.Text.Json;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.TwitchStats;

internal sealed record TwitchStatsWidgetOptions
{
	public static readonly TwitchStatsWidgetOptions Default = new();

	public string Style { get; init; } = TwitchStatsStyles.Overview;

	public string Metric { get; init; } = TwitchStatsMetrics.Viewers;

	public IReadOnlyList<string> Tiles { get; init; } = TwitchStatsMetrics.DefaultTiles;

	public IReadOnlyList<string> Details { get; init; } = TwitchStatsDetails.All;

	public bool ShowThumbnail { get; init; } = true;
}

internal static class TwitchStatsWidgetSettings
{
	public static string? Account(JsonElement data)
		=> WidgetConfigJson.ReadString(data, TwitchStatsWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;

	public static TwitchStatsWidgetOptions Options(JsonElement data)
		=> new()
		{
			Style = TwitchStatsStyles.Normalize(WidgetConfigJson.ReadString(data, TwitchStatsWidgetType.StyleKey)),
			Metric = TwitchStatsMetrics.Normalize(WidgetConfigJson.ReadString(data, TwitchStatsWidgetType.MetricKey)),
			Tiles = ReadList(data, TwitchStatsWidgetType.TilesKey, TwitchStatsMetrics.All) ??
				TwitchStatsMetrics.DefaultTiles,
			Details = ReadList(data, TwitchStatsWidgetType.DetailsKey, TwitchStatsDetails.All) ?? TwitchStatsDetails.All,
			ShowThumbnail = WidgetConfigJson.ReadBool(data, TwitchStatsWidgetType.ThumbnailKey) ?? true,
		};

	public static string? BackgroundColor(JsonElement data)
		=> WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, "backgroundColor"));

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
