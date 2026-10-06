using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Calendar;

public sealed record CalendarWidgetEventFilter(IReadOnlyList<string> Calendars, bool ShowAllDay, TimeSpan LeadTime)
{
	public bool Accepts(CalendarEventSummary calendarEvent)
		=> (ShowAllDay || !calendarEvent.IsAllDay) &&
			(Calendars.Count == 0 || Calendars.Contains(calendarEvent.CalendarKey));
}

public static class CalendarWidgetSelection
{
	private static readonly string[] _pressTriggers =
	[
		WidgetTriggerTypes.ShortPress, WidgetTriggerTypes.LongPress, WidgetTriggerTypes.DoublePress,
		WidgetTriggerTypes.TouchStart, WidgetTriggerTypes.TouchEnd,
	];

	public static IReadOnlyList<string> OwnTriggers { get; } =
	[
		WidgetTriggerTypes.CalendarEventStartsSoon, WidgetTriggerTypes.CalendarEventStarted,
		WidgetTriggerTypes.CalendarEventEnded,
	];

	public static IReadOnlyList<string> FlowTriggers { get; } = [.. _pressTriggers, .. OwnTriggers];

	public static CalendarEventSummary? ShownEvent(
		string widgetType,
		string? widgetData,
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		TimeZoneInfo timeZone)
	{
		if (!CalendarWidgetTypes.IsCalendarWidget(widgetType))
		{
			return null;
		}

		var data = CalendarWidgetData.Parse(widgetData);

		return CalendarWidgetData.IsNextEvent(data)
			? NextEvent(snapshot, now, CalendarNextEventSettings.Parse(data))
			: AgendaEvent(snapshot, now, CalendarAgendaSettings.Parse(data), timeZone);
	}

	public static CalendarEventSummary? NextEvent(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		CalendarNextEventSettings settings)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(settings);

		return snapshot.Events.FirstOrDefault(calendarEvent =>
			(settings.ShowAllDay || !calendarEvent.IsAllDay) &&
			(settings.Calendars.Count == 0 || settings.Calendars.Contains(calendarEvent.CalendarKey)) &&
			(settings.SkipRunning ? calendarEvent.Start > now : calendarEvent.Overlaps(now, DateTimeOffset.MaxValue)));
	}

	public static CalendarEventSummary? AgendaEvent(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		CalendarAgendaSettings settings,
		TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(settings);

		var end = CalendarTime.StartOfDay(CalendarTime.LocalDate(now, timeZone).AddDays(settings.Days), timeZone);

		return snapshot.EventsBetween(now, end, settings.Calendars, settings.ShowAllDay).FirstOrDefault();
	}

	public static CalendarWidgetEventFilter? EventFilter(string widgetType, string? widgetData)
	{
		if (!CalendarWidgetTypes.IsCalendarWidget(widgetType))
		{
			return null;
		}

		var data = CalendarWidgetData.Parse(widgetData);
		var lead = TimeSpan.FromMinutes(CalendarWidgetData.LeadTimeMinutes(data));

		if (CalendarWidgetData.IsNextEvent(data))
		{
			var next = CalendarNextEventSettings.Parse(data);
			return new CalendarWidgetEventFilter(next.Calendars, next.ShowAllDay, lead);
		}

		var agenda = CalendarAgendaSettings.Parse(data);
		return new CalendarWidgetEventFilter(agenda.Calendars, agenda.ShowAllDay, lead);
	}
}
