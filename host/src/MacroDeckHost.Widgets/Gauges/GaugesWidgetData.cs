using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Ui.Config;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Widgets.Gauges;

public sealed record GaugesWidgetData
{
	public const int MaxGauges = 8;

	public const string StyleRing = "ring";
	public const string StyleArc = "arc";

	public string? Title { get; init; }

	public string Style { get; init; } = StyleRing;

	public string? BackgroundColor { get; init; }

	public IReadOnlyList<GaugeConfig> Gauges { get; init; } = [];

	public bool IsArc => Style == StyleArc;

	public IReadOnlyList<GaugeConfig> Shown => Gauges.Count > MaxGauges ? Gauges.Take(MaxGauges).ToList() : Gauges;

	public static GaugesWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new GaugesWidgetData();
		}

		return new GaugesWidgetData
		{
			Title = ReadString(data, "title") is { Length: > 0 } title ? title : null,
			Style = ReadString(data, "style") == StyleArc ? StyleArc : StyleRing,
			BackgroundColor = WidgetColor.NormalizeBackground(ReadString(data, "backgroundColor")),
			Gauges = ParseGauges(data.TryGetProperty("gauges", out var gauges) ? gauges : default),
		};
	}

	public static IReadOnlyList<GaugeConfig> ParseGauges(JsonElement gauges)
	{
		if (gauges.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<GaugeConfig>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var index = 0;

		foreach (var item in gauges.EnumerateArray())
		{
			index++;

			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var id = ReadString(item, "id") is { } stored && IsUsableId(stored) && seen.Add(stored) ? stored : null;

			if (id is null)
			{
				id = $"gauge-{index}";
				while (!seen.Add(id))
				{
					id += "-";
				}
			}

			result.Add(GaugeConfig.Parse(item, id));
		}

		return result;
	}

	// Ids become node keys in the editor tree, which only accepts short identifiers.
	private static bool IsUsableId(string id)
		=> id.Length is > 0 and <= 32 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

	internal static string? ReadString(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	internal static double? ReadDouble(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? number
				: null;
}

public sealed record GaugeConfig
{
	public const string WarnAbove = "above";
	public const string WarnBelow = "below";

	public required string Id { get; init; }

	public string Variable { get; init; } = string.Empty;

	public string? Name { get; init; }

	public WidgetIconReference? Icon { get; init; }

	public double? Min { get; init; }

	public double? Max { get; init; }

	public string? Color { get; init; }

	public string? IconColor { get; init; }

	public string? WarnWhen { get; init; }

	public double? WarnAt { get; init; }

	public bool ThresholdsEnabled { get; init; }

	public UiThresholds? Thresholds { get; init; }

	public static GaugeConfig Parse(JsonElement item, string id)
	{
		var warnWhen = GaugesWidgetData.ReadString(item, "warnWhen");

		return new GaugeConfig
		{
			Id = id,
			Variable = GaugesWidgetData.ReadString(item, "variable") ?? string.Empty,
			Name = GaugesWidgetData.ReadString(item, "name") is { Length: > 0 } name ? name : null,
			Icon = item.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.Object
				? WidgetIconReference.Read(JsonNode.Parse(icon.GetRawText()), null)
				: null,
			Min = GaugesWidgetData.ReadDouble(item, "min"),
			Max = GaugesWidgetData.ReadDouble(item, "max"),
			Color = WidgetColor.Normalize(GaugesWidgetData.ReadString(item, "color")),
			IconColor = WidgetColor.Normalize(GaugesWidgetData.ReadString(item, "iconColor")),
			WarnWhen = warnWhen is WarnAbove or WarnBelow ? warnWhen : null,
			WarnAt = GaugesWidgetData.ReadDouble(item, "warnAt"),
			ThresholdsEnabled = WidgetThresholds.ReadEnabled(item),
			Thresholds = WidgetThresholds.ReadStored(item),
		};
	}
}
