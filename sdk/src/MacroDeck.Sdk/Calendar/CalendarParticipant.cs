namespace MacroDeck.Sdk.Calendar;

/// <summary>Someone invited to a <see cref="CalendarEvent" />. At least one of <see cref="Name" /> and
/// <see cref="Email" /> should be set.</summary>
public sealed record CalendarParticipant
{
	public string? Name { get; init; }

	public string? Email { get; init; }

	/// <summary>Whether this person organizes the event.</summary>
	public bool IsOrganizer { get; init; }

	/// <summary>How this person answered the invitation.</summary>
	public CalendarResponseStatus Response { get; init; }
}
