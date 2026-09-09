using System.Text.Json;

namespace MacroDeckHost.Widgets.HistoryGraph;

public sealed record HistoryGraphWidgetData
{
	/// <summary>How many samples a graph retains when the configuration names no length.</summary>
	public const int DefaultHistoryLength = 40;

	/// <summary>The numeric variable plotted and shown large.</summary>
	public string ValueVariable { get; init; } = string.Empty;

	public string? Title { get; init; }

	public string? Subtitle { get; init; }

	public bool ShowSubtitle { get; init; } = true;

	/// <summary>The chart colour, as <c>#rrggbb</c>. Absent means the reader's own accent.</summary>
	public string? AccentColor { get; init; }

	/// <summary>A fixed upper bound for the chart's scale. Absent scales to the retained window.</summary>
	public double? MaxValue { get; init; }

	/// <summary>A fixed lower bound for the chart's scale, which may be negative. Absent, or zero, scales to
	/// the retained window - the editor's number field has no unset state and spells "auto" as zero.</summary>
	public double? MinValue { get; init; }

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
			Subtitle = SubtitleTextOf(ReadString(data, "subtitle"), ReadString(data, "subtitleVariable")),
			ShowSubtitle = ReadBool(data, "showSubtitle") ?? true,
			AccentColor = ReadHexColor(data, "accentColor"),
			MaxValue = ReadDouble(data, "maxValue"),
			MinValue = ReadDouble(data, "minValue"),

			// A length of one would leave the chart a single flat segment forever, so anything at or below
			// it reads as "unset" rather than as a one-sample graph - the same floor the client applied.
			HistoryLength = historyLength is > 1 and <= int.MaxValue
				? (int)Math.Floor(historyLength.Value)
				: DefaultHistoryLength,
		};
	}

	public static string VariableToken(string variableName) => $"{{{{ vars.{variableName} }}}}";

	// An empty subtitle is an answer rather than an absence: the user cleared it, and the key it
	// replaced must not resurrect it.
	public static string? SubtitleTextOf(string? subtitle, string? subtitleVariable)
	{
		if (subtitle is not null)
		{
			return subtitle;
		}

		// Nothing constrains the older key to a variable name, so a hand written profile can hold text
		// that would interpolate into broken liquid.
		return IsVariableName(subtitleVariable) ? VariableToken(subtitleVariable!) : null;
	}

	private static bool IsVariableName(string? name)
	{
		if (string.IsNullOrEmpty(name) || char.IsAsciiDigit(name[0]))
		{
			return false;
		}

		foreach (var character in name)
		{
			if (!char.IsAsciiLetterOrDigit(character) && character != '_')
			{
				return false;
			}
		}

		return true;
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
