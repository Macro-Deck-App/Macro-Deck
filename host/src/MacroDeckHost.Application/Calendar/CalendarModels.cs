using MacroDeck.Localization;
using MacroDeck.Sdk.Calendar;

namespace MacroDeckHost.Application.Calendar;

public sealed record CalendarAccountDescriptor(
	string AccountId,
	string IntegrationId,
	string LocalAccountId,
	LocalizedText ProviderName,
	string DisplayName);

public enum CalendarAccountStatus
{
	Ok,

	Error
}

public sealed record CalendarSummary(
	string Key,
	string AccountId,
	string CalendarId,
	string Name,
	string? Color,
	bool IsPrimary);

public sealed record CalendarAccountState(
	CalendarAccountDescriptor Account,
	CalendarAccountStatus Status,
	IReadOnlyList<CalendarSummary> Calendars);

public sealed record CalendarEventSummary
{
	public required string InstanceKey { get; init; }

	public required string EventId { get; init; }

	public required string AccountId { get; init; }

	public required string AccountName { get; init; }

	public required LocalizedText ProviderName { get; init; }

	public required string IntegrationId { get; init; }

	public required string CalendarKey { get; init; }

	public required string CalendarId { get; init; }

	public required string CalendarName { get; init; }

	public string? CalendarColor { get; init; }

	public required string Title { get; init; }

	public required DateTimeOffset Start { get; init; }

	public required DateTimeOffset End { get; init; }

	public bool IsAllDay { get; init; }

	public string? Location { get; init; }

	public string? MeetingUrl { get; init; }

	public bool IsRunningAt(DateTimeOffset instant) => Start <= instant && instant < End;

	public bool Overlaps(DateTimeOffset from, DateTimeOffset to)
		=> Start < to && (End > from || (End == Start && Start >= from));
}

public sealed record CalendarEventDetails(
	CalendarEventSummary Summary,
	string? Description,
	IReadOnlyList<CalendarParticipant> Participants);

public sealed record CalendarSnapshot(
	DateTimeOffset WindowStart,
	DateTimeOffset WindowEnd,
	IReadOnlyList<CalendarAccountState> Accounts,
	IReadOnlyList<CalendarEventSummary> Events)
{
	public static CalendarSnapshot Empty { get; } = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue, [], []);

	public IEnumerable<CalendarSummary> Calendars => Accounts.SelectMany(account => account.Calendars);

	public CalendarEventSummary? FindEvent(string calendarKey, string eventId)
		=> Events.FirstOrDefault(e => e.CalendarKey == calendarKey && e.EventId == eventId);

	public IEnumerable<CalendarEventSummary> EventsBetween(
		DateTimeOffset from,
		DateTimeOffset to,
		IReadOnlyCollection<string>? calendarKeys = null,
		bool includeAllDay = true)
		=> Events.Where(e => e.Overlaps(from, to) &&
			(includeAllDay || !e.IsAllDay) &&
			(calendarKeys is null || calendarKeys.Count == 0 || calendarKeys.Contains(e.CalendarKey)));

	internal bool SameContentAs(CalendarSnapshot other)
		=> WindowStart == other.WindowStart &&
			WindowEnd == other.WindowEnd &&
			Events.SequenceEqual(other.Events) &&
			Accounts.Count == other.Accounts.Count &&
			Accounts.Zip(other.Accounts)
				.All(pair => pair.First.Account == pair.Second.Account &&
					pair.First.Status == pair.Second.Status &&
					pair.First.Calendars.SequenceEqual(pair.Second.Calendars));
}
