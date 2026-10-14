using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Calendar;
using MacroDeck.Sdk.Locale;
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

	public DateOnly Date(DateTimeOffset instant)
	{
		return CalendarTime.LocalDate(instant, TimeZone);
	}

	public string Time(DateTimeOffset instant)
	{
		return _time.Format(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime));
	}

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
	{
		return day == today || day == today.AddDays(1)
			? DayName(day, today)
			: Strings.DayHeading(day.ToString("dddd", Culture),
				day.ToString(Culture.DateTimeFormat.MonthDayPattern, Culture));
	}

	public string Weekday(DateOnly day)
	{
		return day.ToString("dddd", Culture).ToUpper(Culture);
	}

	public string DayOfMonth(DateOnly day)
	{
		return day.Day.ToString(Culture);
	}

	public LocalizedText DateLine(DateOnly day)
	{
		return Strings.DateLine(Weekday(day), DayOfMonth(day));
	}

	public string LongDate(DateOnly day)
	{
		return day.ToString(Culture.DateTimeFormat.LongDatePattern, Culture);
	}

	public LocalizedText AgendaTime(CalendarEventSummary calendarEvent, DateOnly day)
	{
		if (calendarEvent.IsAllDay)
		{
			return Strings.AllDay();
		}

		return Date(calendarEvent.Start) < day
			? Strings.Until(Time(calendarEvent.End))
			: Time(calendarEvent.Start);
	}

	public LocalizedText WidgetRange(CalendarEventSummary calendarEvent, DateOnly today)
	{
		var day = Date(calendarEvent.Start);

		if (calendarEvent.IsAllDay)
		{
			return day <= today ? Strings.AllDay() : Strings.DayAllDay(DayName(day, today));
		}

		var range = Strings.TimeRange(Time(calendarEvent.Start), Time(calendarEvent.End));

		return day <= today
			? range
			: Strings.DayTimeRange(DayName(day, today),
				Time(calendarEvent.Start),
				Time(calendarEvent.End));
	}

	public LocalizedText DetailsRange(CalendarEventSummary calendarEvent)
	{
		var startDay = Date(calendarEvent.Start);

		if (calendarEvent.IsAllDay)
		{
			var lastDay = Date(calendarEvent.End).AddDays(-1);

			return lastDay <= startDay
				? Strings.Details.AllDayDate(LongDate(startDay))
				: Strings.Details.DateRange(LongDate(startDay), LongDate(lastDay));
		}

		var endDay = Date(calendarEvent.End);

		return endDay == startDay
			? Strings.Details.TimedRange(LongDate(startDay),
				Time(calendarEvent.Start),
				Time(calendarEvent.End))
			: Strings.Details.TimedRangeAcrossDays(LongDate(startDay),
				Time(calendarEvent.Start),
				LongDate(endDay),
				Time(calendarEvent.End));
	}

	public static LocalizedText Title(string? title)
	{
		return CalendarCountdown.Title(title);
	}
}
