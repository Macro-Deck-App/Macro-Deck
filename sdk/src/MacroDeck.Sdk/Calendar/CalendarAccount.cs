namespace MacroDeck.Sdk.Calendar;

/// <summary>
/// One account a <see cref="ICalendarProvider" /> reads, e.g. a connected Google account. See
/// <see cref="ICalendarProvider.GetAccounts" /> for the rules <see cref="Id" /> has to follow.
/// </summary>
public sealed record CalendarAccount
{
	/// <summary>Unique within the provider and stable across restarts.</summary>
	public required string Id { get; init; }

	/// <summary>Shown to the user, e.g. the account's email address.</summary>
	public required string DisplayName { get; init; }
}
