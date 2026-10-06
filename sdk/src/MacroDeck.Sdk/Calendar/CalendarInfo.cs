namespace MacroDeck.Sdk.Calendar;

/// <summary>One calendar of a <see cref="CalendarAccount" />.</summary>
public sealed record CalendarInfo
{
	/// <summary>Unique within the account and stable across reads. Otherwise opaque to the host.</summary>
	public required string Id { get; init; }

	/// <summary>Shown to the user.</summary>
	public required string Name { get; init; }

	/// <summary>The calendar's colour as <c>#RRGGBB</c>, or <c>null</c> for none. The host ignores any other
	/// format.</summary>
	public string? Color { get; init; }

	/// <summary>Whether this is the account's main calendar.</summary>
	public bool IsPrimary { get; init; }
}
