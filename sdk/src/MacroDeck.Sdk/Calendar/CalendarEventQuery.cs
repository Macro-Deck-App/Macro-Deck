namespace MacroDeck.Sdk.Calendar;

/// <summary>What <see cref="ICalendarProvider.GetEventsAsync" /> reads: the events overlapping
/// <see cref="From" /> up to, but not including, <see cref="To" />.</summary>
public sealed record CalendarEventQuery
{
	/// <summary>Start of the range, inclusive.</summary>
	public required DateTimeOffset From { get; init; }

	/// <summary>End of the range, exclusive.</summary>
	public required DateTimeOffset To { get; init; }

	/// <summary>The <see cref="CalendarInfo.Id" />s to read. Empty means every calendar of the
	/// account.</summary>
	public IReadOnlyList<string> CalendarIds { get; init; } = [];
}
