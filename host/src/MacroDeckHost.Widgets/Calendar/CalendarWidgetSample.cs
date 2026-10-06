using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar.Sample;

namespace MacroDeckHost.Widgets.Calendar;

internal static class CalendarWidgetSample
{
	private const string AccountId = "sample";
	private const string CalendarId = "work";

	public static async Task<CalendarSnapshot> BuildAsync(
		IWidgetSampleTextResolver text,
		DateTimeOffset now,
		TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(text);

		var calendarName = await text.ResolveAsync(Strings.WorkCalendar()).ConfigureAwait(false);
		var standup = await text.ResolveAsync(Strings.Standup()).ConfigureAwait(false);
		var review = await text.ResolveAsync(Strings.DesignReview()).ConfigureAwait(false);
		var lunch = await text.ResolveAsync(Strings.Lunch()).ConfigureAwait(false);
		var offsite = await text.ResolveAsync(Strings.Offsite()).ConfigureAwait(false);
		var room = await text.ResolveAsync(Strings.Room()).ConfigureAwait(false);

		var account = new CalendarAccountDescriptor(AccountId,
			CalendarWidgetTypes.OwnerId,
			AccountId,
			AppStrings.Integrations.Calendar.Name(),
			"you@example.com");
		var calendar = new CalendarSummary(CalendarKeys.Calendar(AccountId, CalendarId),
			AccountId,
			CalendarId,
			calendarName,
			"#4f8ef7",
			true);

		var local = TimeZoneInfo.ConvertTime(now, timeZone);
		var nextHalfHour = local.AddMinutes(30 - (local.Minute % 30)).AddSeconds(-local.Second)
			.AddMilliseconds(-local.Millisecond);
		var today = CalendarTime.LocalDate(now, timeZone);
		var tomorrowNoon = CalendarTime.StartOfDay(today.AddDays(1), timeZone).AddHours(12).AddMinutes(30);

		CalendarEventSummary Event(string id, string title, DateTimeOffset start, TimeSpan duration,
			bool allDay = false, string? location = null)
			=> new()
			{
				InstanceKey = CalendarKeys.EventInstance(calendar.Key, id),
				EventId = id,
				AccountId = AccountId,
				AccountName = account.DisplayName,
				ProviderName = account.ProviderName,
				IntegrationId = CalendarWidgetTypes.OwnerId,
				CalendarKey = calendar.Key,
				CalendarId = CalendarId,
				CalendarName = calendar.Name,
				CalendarColor = calendar.Color,
				Title = title,
				Start = start,
				End = start + duration,
				IsAllDay = allDay,
				Location = location,
			};

		var startOfToday = CalendarTime.StartOfDay(today, timeZone);

		IReadOnlyList<CalendarEventSummary> events =
		[
			Event("offsite", offsite, startOfToday, CalendarTime.StartOfDay(today.AddDays(1), timeZone) - startOfToday,
				allDay: true),
			Event("standup", standup, nextHalfHour, TimeSpan.FromMinutes(15), location: room),
			Event("review", review, nextHalfHour.AddHours(2), TimeSpan.FromHours(1), location: room),
			Event("lunch", lunch, tomorrowNoon, TimeSpan.FromHours(1)),
		];

		var (from, to) = CalendarTime.SyncWindow(now, timeZone);

		return new CalendarSnapshot(from,
			to,
			[new CalendarAccountState(account, CalendarAccountStatus.Ok, [calendar])],
			events);
	}
}
