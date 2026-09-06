using System.Text.Json;

namespace MacroDeckHost.Widgets.HistoryGraph;

public sealed record HistoryGraphWidgetData
{
	/// <summary>How many samples a graph retains when the configuration names no length.</summary>
	public const int DefaultHistoryLength = 40;

	/// <summary>The numeric variable plotted and shown large.</summary>
	public string ValueVariable { get; init; } = string.Empty;

	public string? Title { get; init; }

	/// <summary>A text variable shown as the subtitle. Falls back to <see cref="Subtitle" /> whenever it
	/// resolves to nothing.</summary>
	public string? SubtitleVariable { get; init; }

	public string? Subtitle { get; init; }

	public bool ShowSubtitle { get; init; } = true;

	/// <summary>The chart colour, as <c>#rrggbb</c>. Absent means the reader's own accent.</summary>
	public string? AccentColor { get; init; }

	/// <summary>A fixed upper bound for the chart's scale. Absent scales to the retained window.</summary>
	public double? MaxValue { get; init; }

	public int HistoryLength { get; init; } = DefaultHistoryLength;

	public static HistoryGraphWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new HistoryGraphWidgetData();
		}

		var historyLength = ReadDouble(data, "historyLength");

		return new HistoryGraphWidgetData
		{
			ValueVariable = ReadString(data, "valueVariable") ?? string.Empty,
			Title = ReadString(data, "title"),
			SubtitleVariable = ReadString(data, "subtitleVariable"),
			Subtitle = ReadString(data, "subtitle"),
			ShowSubtitle = ReadBool(data, "showSubtitle") ?? true,
			AccentColor = ReadHexColor(data, "accentColor"),
			MaxValue = ReadDouble(data, "maxValue"),

			// A length of one would leave the chart a single flat segment forever, so anything at or below
			// it reads as "unset" rather than as a one-sample graph - the same floor the client applied.
			HistoryLength = historyLength is > 1 and <= int.MaxValue
				? (int)Math.Floor(historyLength.Value)
				: DefaultHistoryLength,
		};
	}

	private static string? ReadString(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	/// <summary>
	/// A colour only if it is one the widget profile can carry. The editor's reset swatch stores the
	/// client's own <c>var(--color-accent)</c> sentinel in older profiles, and a widget tree spells "the
	/// reader's accent" as an absent colour rather than as a literal a reader would have to know.
	/// </summary>
	private static string? ReadHexColor(JsonElement data, string name) => WidgetColor.Normalize(ReadString(data, name));

	private static bool? ReadBool(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;

	private static double? ReadDouble(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? number
				: null;
}
