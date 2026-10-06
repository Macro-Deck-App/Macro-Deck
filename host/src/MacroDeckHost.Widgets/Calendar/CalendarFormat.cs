using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Widgets.Calendar;

internal sealed class CalendarFormat
{
	private readonly TimeOfDayFormat _time;

	public CalendarFormat(TimeOfDayFormat time, TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(timeZone);

		_time = time;
		TimeZone = timeZone;
	}

	public TimeZoneInfo TimeZone { get; }

	private CultureInfo Culture => _time.Culture;

	public DateOnly Date(DateTimeOffset instant) => CalendarTime.LocalDate(instant, TimeZone);

	public string Time(DateTimeOffset instant)
		=> _time.Format(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime));

	public LocalizedText DayName(DateOnly day, DateOnly today)
	{
		if (day == today)
		{
			return Strings.Today();
		}

		return day == today.AddDays(1)
			? Strings.Tomorrow()
			: day.ToString("dddd", Culture);
	}

	public LocalizedText DayHeading(DateOnly day, DateOnly today)
		=> day == today || day == today.AddDays(1)
			? DayName(day, today)
			: Strings.DayHeading(weekday: day.ToString("dddd", Culture),
				date: day.ToString(Culture.DateTimeFormat.MonthDayPattern, Culture));

	public string Weekday(DateOnly day) => day.ToString("dddd", Culture).ToUpper(Culture);

	public string DayOfMonth(DateOnly day) => day.Day.ToString(Culture);

	public LocalizedText DateLine(DateOnly day) => Strings.DateLine(weekday: Weekday(day), day: DayOfMonth(day));

	public string LongDate(DateOnly day) => day.ToString(Culture.DateTimeFormat.LongDatePattern, Culture);

	public LocalizedText AgendaTime(CalendarEventSummary calendarEvent, DateOnly day)
	{
		if (calendarEvent.IsAllDay)
		{
			return Strings.AllDay();
		}

		return Date(calendarEvent.Start) < day
			? Strings.Until(time: Time(calendarEvent.End))
			: Time(calendarEvent.Start);
	}

	public LocalizedText WidgetRange(CalendarEventSummary calendarEvent, DateOnly today)
	{
		var day = Date(calendarEvent.Start);

		if (calendarEvent.IsAllDay)
		{
			return day <= today ? Strings.AllDay() : Strings.DayAllDay(day: DayName(day, today));
		}

		var range = Strings.TimeRange(start: Time(calendarEvent.Start), end: Time(calendarEvent.End));

		return day <= today ? range : Strings.DayTimeRange(day: DayName(day, today), start: Time(calendarEvent.Start),
			end: Time(calendarEvent.End));
	}

	public LocalizedText DetailsRange(CalendarEventSummary calendarEvent)
	{
		var startDay = Date(calendarEvent.Start);

		if (calendarEvent.IsAllDay)
		{
			var lastDay = Date(calendarEvent.End).AddDays(-1);

			return lastDay <= startDay
				? Strings.Details.AllDayDate(date: LongDate(startDay))
				: Strings.Details.DateRange(start: LongDate(startDay), end: LongDate(lastDay));
		}

		var endDay = Date(calendarEvent.End);

		return endDay == startDay
			? Strings.Details.TimedRange(date: LongDate(startDay),
				start: Time(calendarEvent.Start),
				end: Time(calendarEvent.End))
			: Strings.Details.TimedRangeAcrossDays(startDate: LongDate(startDay),
				startTime: Time(calendarEvent.Start),
				endDate: LongDate(endDay),
				endTime: Time(calendarEvent.End));
	}

	public static LocalizedText Title(string? title) => CalendarCountdown.Title(title);
}
