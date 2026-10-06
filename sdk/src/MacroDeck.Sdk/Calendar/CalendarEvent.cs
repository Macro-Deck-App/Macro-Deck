namespace MacroDeck.Sdk.Calendar;

/// <summary>One event, or one occurrence of a recurring event, in a calendar.</summary>
public sealed record CalendarEvent
{
	/// <summary>
	/// Unique within the calendar and stable across reads. Each occurrence of a recurring event has an id
	/// of its own. Otherwise opaque to the host.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>The <see cref="CalendarInfo.Id" /> of the calendar this event belongs to.</summary>
	public required string CalendarId { get; init; }

	public required string Title { get; init; }

	/// <summary>When the event starts. For an all-day event only its date counts, see
	/// <see cref="IsAllDay" />.</summary>
	public required DateTimeOffset Start { get; init; }

	/// <summary>When the event ends. For an all-day event only its date counts, see
	/// <see cref="IsAllDay" />.</summary>
	public required DateTimeOffset End { get; init; }

	/// <summary>
	/// Whether the event fills whole days. Then only the dates of <see cref="Start" /> and
	/// <see cref="End" />, each in its own offset, count: the event covers <c>Start.Date</c> up to, but not
	/// including, <c>End.Date</c>, and the host places those days in its own local time zone. A one-day
	/// event on 5 October starts on 5 October and ends on 6 October.
	/// </summary>
	public bool IsAllDay { get; init; }

	public string? Location { get; init; }

	/// <summary>Plain text or HTML. The host removes markup before it shows the text, so do not rely on
	/// markup for meaning.</summary>
	public string? Description { get; init; }

	/// <summary>The link to join the event's online meeting, or <c>null</c> for none. Only an absolute
	/// <c>http</c> or <c>https</c> URL is used; the host ignores anything else.</summary>
	public string? MeetingUrl { get; init; }

	/// <summary>The people invited, including the organizer. Empty when the provider reports none.</summary>
	public IReadOnlyList<CalendarParticipant> Participants { get; init; } = [];
}
