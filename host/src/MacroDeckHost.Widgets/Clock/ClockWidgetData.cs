using System.Text.Json;

namespace MacroDeckHost.Widgets.Clock;

public sealed record ClockWidgetData
{
	public bool IsAnalog { get; init; }

	public string? TimeZone { get; init; }

	public string? Label { get; init; }

	public bool ShowLabel { get; init; }

	public bool ShowSeconds { get; init; } = true;

	public bool ShowDate { get; init; } = true;

	public bool ShowOffset { get; init; }

	public string? BackgroundColor { get; init; }

	public string? TextColor { get; init; }

	public ClockHourCycle HourCycle { get; init; } = ClockHourCycle.Auto;

	public bool LeadingZero { get; init; } = true;

	public ClockDateFormat DateFormat { get; init; } = ClockDateFormat.Default;

	public ClockDatePosition DatePosition { get; init; } = ClockDatePosition.Below;

	public static ClockWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new ClockWidgetData();
		}

		var timeZone = Trimmed(ReadString(data, "timeZone"));

		return new ClockWidgetData
		{
			IsAnalog = string.Equals(ReadString(data, "style"), "analog", StringComparison.Ordinal),
			TimeZone = timeZone,
			Label = Trimmed(ReadString(data, "label")),
			ShowLabel = ReadBool(data, "showLabel") ?? timeZone is not null,
			ShowSeconds = ReadBool(data, "showSeconds") ?? true,
			ShowDate = ReadBool(data, "showDate") ?? true,
			ShowOffset = ReadBool(data, "showOffset") ?? false,
			BackgroundColor = Trimmed(ReadString(data, "backgroundColor")),
			TextColor = Trimmed(ReadString(data, "textColor")),
			HourCycle = ReadString(data, "hourCycle") switch
			{
				"12h" => ClockHourCycle.TwelveHour,
				"24h" => ClockHourCycle.TwentyFourHour,
				_ => ClockHourCycle.Auto,
			},
			LeadingZero = ReadBool(data, "leadingZero") ?? true,
			DateFormat = ReadString(data, "dateFormat") switch
			{
				"day-first" => ClockDateFormat.DayFirst,
				"month-first" => ClockDateFormat.MonthFirst,
				"iso" => ClockDateFormat.Iso,
				"long" => ClockDateFormat.Written,
				_ => ClockDateFormat.Default,
			},
			DatePosition = ReadString(data, "datePosition") switch
			{
				"above" => ClockDatePosition.Above,
				"left" => ClockDatePosition.Left,
				"right" => ClockDatePosition.Right,
				_ => ClockDatePosition.Below,
			},
		};
	}

	private static string? Trimmed(string? value)
		=> string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static string? ReadString(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool? ReadBool(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;
}

/// <summary>Which face the time is drawn on. <see cref="Auto" /> leaves the choice to the reader's
/// language, which is what the widget did before the choice existed.</summary>
public enum ClockHourCycle
{
	Auto,
	TwelveHour,
	TwentyFourHour,
}

/// <summary>How the date line is written. <see cref="Default" /> is the reader's own short date.</summary>
public enum ClockDateFormat
{
	Default,
	DayFirst,
	MonthFirst,
	Iso,
	Written,
}

/// <summary>Where the date line sits relative to the time.</summary>
public enum ClockDatePosition
{
	Below,
	Above,
	Left,
	Right,
}
