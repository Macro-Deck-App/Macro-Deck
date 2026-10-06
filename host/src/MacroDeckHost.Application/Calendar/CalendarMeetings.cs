using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Caching;

namespace MacroDeckHost.Application.Calendar;

public sealed record CalendarHostServices(
	ICalendarEventCache Events,
	IExternalUrlOpener UrlOpener,
	TimeProvider Time,
	IFolderCache Folders);

public static class CalendarMeetings
{
	public static readonly TimeSpan DefaultJoinWindow = TimeSpan.FromMinutes(15);

	public static CalendarEventSummary? FindJoinable(
		CalendarSnapshot snapshot,
		DateTimeOffset now,
		TimeSpan window,
		string? calendarKey = null)
	{
		if (window < TimeSpan.Zero)
		{
			window = TimeSpan.Zero;
		}

		return snapshot.Events
			.Where(e => !e.IsAllDay &&
				e.MeetingUrl is not null &&
				(string.IsNullOrEmpty(calendarKey) || e.CalendarKey == calendarKey) &&
				e.Start - window <= now &&
				now < e.End)
			.OrderBy(e => (e.Start - now).Duration())
			.ThenBy(e => e.Start)
			.FirstOrDefault();
	}
}
