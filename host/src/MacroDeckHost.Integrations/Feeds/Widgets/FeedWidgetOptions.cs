using System.Text.Json;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal sealed record FeedWidgetOptions
{
	public string Source { get; init; } = FeedWidgetTypes.AllSource;

	public Guid? FeedId { get; init; }

	public string? Group { get; init; }

	public int Count { get; init; } = 5;

	public bool ShowDescription { get; init; }

	public bool ShowThumbnail { get; init; } = true;

	public int CycleSeconds { get; init; }

	public string Tap { get; init; } = FeedWidgetTypes.OpenTap;

	public static FeedWidgetOptions Defaults(FeedWidgetKind kind)
	{
		return kind == FeedWidgetKind.List
			? new FeedWidgetOptions { Count = 4 }
			: new FeedWidgetOptions { Count = 5, ShowDescription = true };
	}

	public static FeedWidgetOptions Parse(FeedWidgetKind kind, JsonElement data)
	{
		var defaults = Defaults(kind);
		var source = ReadString(data, FeedWidgetTypes.SourceKey);

		return new FeedWidgetOptions
		{
			Source = source is not null && FeedWidgetTypes.Sources.Contains(source, StringComparer.Ordinal)
				? source
				: FeedWidgetTypes.AllSource,
			FeedId = Guid.TryParse(ReadString(data, FeedWidgetTypes.FeedKey), out var feed) ? feed : null,
			Group = ReadString(data, FeedWidgetTypes.GroupKey)?.Trim() is { Length: > 0 } group ? group : null,
			Count = ReadNumber(data, FeedWidgetTypes.CountKey) is { } count
				? (int)Math.Clamp(Math.Round(count), 1, FeedWidgetTypes.MaxCount)
				: defaults.Count,
			ShowDescription = ReadBool(data, FeedWidgetTypes.ShowDescriptionKey) ?? defaults.ShowDescription,
			ShowThumbnail = ReadBool(data, FeedWidgetTypes.ShowThumbnailKey) ?? defaults.ShowThumbnail,
			CycleSeconds = ReadNumber(data, FeedWidgetTypes.CycleSecondsKey) is { } seconds
				? (int)Math.Clamp(Math.Round(seconds), 0, FeedWidgetTypes.MaxCycleSeconds)
				: defaults.CycleSeconds,
			Tap = ReadString(data, FeedWidgetTypes.TapKey) is { } tap
				&& FeedWidgetTypes.Taps.Contains(tap, StringComparer.Ordinal)
					? tap
					: FeedWidgetTypes.OpenTap
		};
	}

	public static string? BackgroundColor(JsonElement data)
	{
		return WidgetBackgroundColor.Normalize(ReadString(data, FeedWidgetTypes.BackgroundColorKey));
	}

	public static string? ReadString(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	public static JsonElement ReadFlows(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(FeedWidgetTypes.FlowsKey, out var flows))
		{
			return EmptyFlows();
		}

		if (flows.ValueKind == JsonValueKind.Array)
		{
			return flows.Clone();
		}

		if (flows.ValueKind != JsonValueKind.String || flows.GetString() is not { Length: > 0 } raw)
		{
			return EmptyFlows();
		}

		try
		{
			using var document = JsonDocument.Parse(raw);
			return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.Clone() : EmptyFlows();
		}
		catch (JsonException)
		{
			return EmptyFlows();
		}
	}

	private static JsonElement EmptyFlows()
	{
		return JsonSerializer.SerializeToElement(Array.Empty<object>());
	}

	private static double? ReadNumber(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& double.IsFinite(value.GetDouble())
				? value.GetDouble()
				: null;
	}

	private static bool? ReadBool(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;
	}
}
