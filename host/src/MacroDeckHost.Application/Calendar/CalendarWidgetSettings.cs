using System.Text.Json;

namespace MacroDeckHost.Application.Calendar;

public sealed record CalendarAgendaSettings(
	IReadOnlyList<string> Calendars,
	int Days,
	bool ShowAllDay,
	bool ShowTime,
	bool ShowLocation,
	bool ShowCalendar,
	bool ShowDate)
{
	public static CalendarAgendaSettings Parse(JsonElement data)
		=> new(CalendarWidgetData.Calendars(data),
			CalendarWidgetData.Days(data),
			CalendarWidgetData.ReadBool(data, CalendarWidgetTypes.ShowAllDayKey) ?? true,
			CalendarWidgetData.ReadBool(data, CalendarWidgetTypes.ShowTimeKey) ?? true,
			CalendarWidgetData.ReadBool(data, CalendarWidgetTypes.ShowLocationKey) ?? false,
			CalendarWidgetData.ReadBool(data, CalendarWidgetTypes.ShowCalendarKey) ?? false,
			CalendarWidgetData.ShowDate(data));
}

public sealed record CalendarNextEventSettings(
	IReadOnlyList<string> Calendars,
	bool ShowAllDay,
	bool SkipRunning,
	bool ShowDate)
{
	public static CalendarNextEventSettings Parse(JsonElement data)
		=> new(CalendarWidgetData.Calendars(data),
			CalendarWidgetData.ReadBool(data, CalendarWidgetTypes.ShowAllDayKey) ?? false,
			CalendarWidgetData.WhenStarted(data) == CalendarWidgetTypes.WhenStartedNext,
			CalendarWidgetData.ShowDate(data));
}

public static class CalendarWidgetData
{
	public static JsonElement Parse(string? data)
	{
		if (string.IsNullOrWhiteSpace(data))
		{
			return default;
		}

		try
		{
			using var document = JsonDocument.Parse(data);
			return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : default;
		}
		catch (JsonException)
		{
			return default;
		}
	}

	public static IReadOnlyList<string> Calendars(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty(CalendarWidgetTypes.CalendarsKey, out var calendars) ||
			calendars.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return
		[
			.. calendars.EnumerateArray()
				.Where(item => item.ValueKind == JsonValueKind.String)
				.Select(item => item.GetString()!)
				.Where(key => key.Length > 0)
				.Distinct(StringComparer.Ordinal),
		];
	}

	public static string Layout(JsonElement data)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(CalendarWidgetTypes.LayoutKey, out var value) &&
			value.ValueKind == JsonValueKind.String &&
			value.GetString() == CalendarWidgetTypes.LayoutNextEvent
				? CalendarWidgetTypes.LayoutNextEvent
				: CalendarWidgetTypes.LayoutAgenda;

	public static bool IsNextEvent(JsonElement data) => Layout(data) == CalendarWidgetTypes.LayoutNextEvent;

	public static bool ShowDate(JsonElement data) => ReadBool(data, CalendarWidgetTypes.ShowDateKey) ?? true;

	public static int Days(JsonElement data)
		=> ReadNumber(data, CalendarWidgetTypes.DaysKey) is { } days
			? (int)Math.Clamp(Math.Round(days), CalendarWidgetTypes.MinDays, CalendarWidgetTypes.MaxDays)
			: CalendarWidgetTypes.MinDays;

	public static int LeadTimeMinutes(JsonElement data)
		=> ReadNumber(data, CalendarWidgetTypes.LeadTimeKey) is { } minutes
			? (int)Math.Clamp(Math.Round(minutes),
				CalendarWidgetTypes.MinLeadTimeMinutes,
				CalendarWidgetTypes.MaxLeadTimeMinutes)
			: CalendarWidgetTypes.DefaultLeadTimeMinutes;

	public static string WhenStarted(JsonElement data)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(CalendarWidgetTypes.WhenStartedKey, out var value) &&
			value.ValueKind == JsonValueKind.String &&
			value.GetString() == CalendarWidgetTypes.WhenStartedNext
				? CalendarWidgetTypes.WhenStartedNext
				: CalendarWidgetTypes.WhenStartedNow;

	public static bool? ReadBool(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;

	private static double? ReadNumber(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? number
				: null;
}
