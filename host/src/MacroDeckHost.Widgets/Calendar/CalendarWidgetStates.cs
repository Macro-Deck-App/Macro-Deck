using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeckHost.Application.Calendar;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Widgets.Calendar;

internal enum CalendarWidgetStatus
{
	NoCalendars,

	Ready
}

internal sealed record CalendarComputed<TState>(TState State, DateTimeOffset NextRefresh);

internal sealed record CalendarAgendaRow(
	string Key,
	CalendarEventSummary Event,
	LocalizedText Time,
	LocalizedText Title,
	string? Location,
	string? Calendar,
	string? Color);

internal sealed record CalendarAgendaDay(string Key, LocalizedText Heading, IReadOnlyList<CalendarAgendaRow> Rows);

internal sealed record CalendarAgendaState(
	CalendarWidgetStatus Status,
	bool HasAccountError,
	IReadOnlyList<CalendarAgendaRow> Compact,
	IReadOnlyList<CalendarAgendaDay> Days,
	string Weekday,
	string DayOfMonth)
{
	public static CalendarAgendaState Empty { get; } =
		new(CalendarWidgetStatus.NoCalendars, false, [], [], string.Empty, string.Empty);

	public bool HasEvents => Days.Any(day => day.Rows.Count > 0);
}

internal sealed record CalendarNextEventState(
	CalendarWidgetStatus Status,
	bool HasAccountError,
	CalendarEventSummary? Event,
	LocalizedText Title,
	LocalizedText When,
	LocalizedText Range,
	string? Location,
	string? Color,
	LocalizedText DateLine)
{
	public static CalendarNextEventState Empty { get; } =
		new(CalendarWidgetStatus.NoCalendars, false, null, default, default, default, null, null, default);
}

internal static class CalendarAgenda
{
	public const int MaxCompactRows = 4;

	public const int MaxRowsPerDay = 6;

	public static CalendarComputed<CalendarAgendaState> Compute(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		CalendarAgendaSettings settings,
		CalendarFormat format)
	{
		var today = format.Date(now);
		var (days, nextRefresh) = DaysAhead(snapshot, now, settings, format, MaxRowsPerDay);

		var compact = days
			.SelectMany((day, index) => day.Rows.Select(row => (Index: index, Row: row)))
			.DistinctBy(entry => entry.Row.Event.InstanceKey)
			.Take(MaxCompactRows)
			.Select(entry => entry.Row with
			{
				Key = "t" + CalendarWidgetStates.KeyOf(entry.Row.Event),
				Time = entry.Index == 0 ? entry.Row.Time : LaterDayTime(entry.Row.Event, today, settings, format)
			})
			.ToList();

		var state = new CalendarAgendaState(StatusOf(snapshot),
			CalendarWidgetStates.HasAccountError(snapshot, settings.Calendars),
			compact,
			days,
			format.Weekday(today),
			format.DayOfMonth(today));

		return new CalendarComputed<CalendarAgendaState>(state, nextRefresh);
	}

	public static (IReadOnlyList<CalendarAgendaDay> Days, DateTimeOffset NextRefresh) DaysAhead(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		CalendarAgendaSettings settings,
		CalendarFormat format,
		int maxRowsPerDay)
	{
		var today = format.Date(now);
		var nextRefresh = CalendarTime.NextMidnight(now, format.TimeZone);
		var days = new List<CalendarAgendaDay>(settings.Days);

		for (var index = 0; index < settings.Days; index++)
		{
			var day = today.AddDays(index);
			var dayStart = CalendarTime.StartOfDay(day, format.TimeZone);
			var dayEnd = CalendarTime.StartOfDay(day.AddDays(1), format.TimeZone);
			var from = dayStart < now ? now : dayStart;

			var rows = snapshot.EventsBetween(from, dayEnd, settings.Calendars, settings.ShowAllDay)
				.Take(maxRowsPerDay)
				.Select(calendarEvent => Row("r" + index.ToString(CultureInfo.InvariantCulture),
					calendarEvent,
					day,
					settings,
					format))
				.ToList();

			foreach (var row in rows)
			{
				if (row.Event.End > now && row.Event.End < nextRefresh)
				{
					nextRefresh = row.Event.End;
				}
			}

			days.Add(new CalendarAgendaDay("day" + index.ToString(CultureInfo.InvariantCulture),
				format.DayHeading(day, today),
				rows));
		}

		return (days, nextRefresh);
	}

	private static CalendarAgendaRow Row(
		string prefix,
		CalendarEventSummary calendarEvent,
		DateOnly day,
		CalendarAgendaSettings settings,
		CalendarFormat format)
		=> new(prefix + CalendarWidgetStates.KeyOf(calendarEvent),
			calendarEvent,
			settings.ShowTime ? format.AgendaTime(calendarEvent, day) : default,
			CalendarFormat.Title(calendarEvent.Title),
			settings.ShowLocation ? calendarEvent.Location : null,
			settings.ShowCalendar ? calendarEvent.CalendarName : null,
			CalendarWidgetStates.Color(calendarEvent));

	private static LocalizedText LaterDayTime(
		CalendarEventSummary calendarEvent,
		DateOnly today,
		CalendarAgendaSettings settings,
		CalendarFormat format)
		=> settings.ShowTime
			? format.WidgetRange(calendarEvent, today)
			: format.DayName(format.Date(calendarEvent.Start), today);

	private static CalendarWidgetStatus StatusOf(CalendarSnapshot snapshot)
		=> snapshot.Accounts.Count == 0 ? CalendarWidgetStatus.NoCalendars : CalendarWidgetStatus.Ready;
}

internal static class CalendarNextEvent
{
	public static CalendarComputed<CalendarNextEventState> Compute(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		CalendarNextEventSettings settings,
		CalendarFormat format)
	{
		var midnight = CalendarTime.NextMidnight(now, format.TimeZone);
		var status = snapshot.Accounts.Count == 0 ? CalendarWidgetStatus.NoCalendars : CalendarWidgetStatus.Ready;
		var hasError = CalendarWidgetStates.HasAccountError(snapshot, settings.Calendars);
		var dateLine = format.DateLine(format.Date(now));

		var shown = CalendarWidgetSelection.NextEvent(snapshot, now, settings);

		if (shown is null)
		{
			return new CalendarComputed<CalendarNextEventState>(
				CalendarNextEventState.Empty with { Status = status, HasAccountError = hasError, DateLine = dateLine },
				midnight);
		}

		var running = shown.Start <= now;
		var change = running ? shown.End : CalendarCountdown.NextChange(shown.Start, now);

		var state = new CalendarNextEventState(status,
			hasError,
			shown,
			CalendarFormat.Title(shown.Title),
			CalendarCountdown.When(shown, now),
			format.WidgetRange(shown, format.Date(now)),
			shown.Location,
			CalendarWidgetStates.Color(shown),
			dateLine);

		return new CalendarComputed<CalendarNextEventState>(state, change < midnight ? change : midnight);
	}
}

internal static class CalendarWidgetStates
{
	public static string KeyOf(CalendarEventSummary calendarEvent)
		=> Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(calendarEvent.InstanceKey)))[..24];

	public static string? Color(CalendarEventSummary calendarEvent) => WidgetColor.Normalize(calendarEvent.CalendarColor);

	public static bool HasAccountError(CalendarSnapshot snapshot, IReadOnlyList<string> calendars)
		=> snapshot.Accounts.Any(state => state.Status == CalendarAccountStatus.Error &&
			(calendars.Count == 0 || calendars.Any(key =>
				CalendarKeys.TryParseCalendar(key, out var accountId, out _) &&
				accountId == state.Account.AccountId)));
}
