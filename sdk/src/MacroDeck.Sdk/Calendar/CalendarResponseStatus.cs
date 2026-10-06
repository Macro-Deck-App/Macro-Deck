namespace MacroDeck.Sdk.Calendar;

/// <summary>How a <see cref="CalendarParticipant" /> answered an invitation.</summary>
public enum CalendarResponseStatus
{
	/// <summary>The provider does not say.</summary>
	Unknown = 0,

	/// <summary>Not answered yet.</summary>
	NeedsAction = 1,

	Accepted = 2,

	Declined = 3,

	Tentative = 4
}
