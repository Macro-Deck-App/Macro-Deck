namespace MacroDeckHost.Application.Calendar;

// Every id and data key here is persisted in profiles and exports, so the spellings are frozen.
public static class CalendarWidgetTypes
{
	public const string OwnerId = "app.macro-deck.calendar";

	public const string LocalId = "calendar";

	public const string QualifiedId = OwnerId + "::" + LocalId;

	public const string LayoutKey = "layout";

	public const string LayoutAgenda = "agenda";

	public const string LayoutNextEvent = "next-event";

	public const string CalendarsKey = "calendars";

	public const string DaysKey = "days";

	public const string ShowAllDayKey = "showAllDay";

	public const string ShowTimeKey = "showTime";

	public const string ShowLocationKey = "showLocation";

	public const string ShowCalendarKey = "showCalendar";

	public const string ShowDateKey = "showDate";

	public const string WhenStartedKey = "whenStarted";

	public const string WhenStartedNow = "now";

	public const string WhenStartedNext = "next";

	public const int MinDays = 1;

	public const int MaxDays = 7;

	public const string LeadTimeKey = "leadTime";

	public const int MinLeadTimeMinutes = 1;

	public const int MaxLeadTimeMinutes = 60;

	public const int DefaultLeadTimeMinutes = 15;

	public static readonly IReadOnlyList<int> LeadTimeOptions = [1, 5, 10, 15, 30, 60];

	public const string ShowDetailsActionId = "show-details";

	public static bool IsCalendarWidget(string? widgetType)
		=> widgetType == QualifiedId;
}

public static class CalendarOptionsSourceIds
{
	public const string Accounts = "macrodeck.calendar-accounts";

	public const string Calendars = "macrodeck.calendar-calendars";
}
