namespace MacroDeck.Plugin.Protocol.Capabilities.Calendar;

/// <summary>Mirrors the SDK's <c>CalendarAccount</c>.</summary>
public sealed record CalendarAccountDto
{
	public required string Id { get; init; }

	public required string DisplayName { get; init; }
}

/// <summary>The full result of the <c>calendar</c> capability's <c>describe</c> operation - what
/// <c>RemotePluginSnapshotRefresher</c> folds into the snapshot's provider name and account list.</summary>
public sealed record CalendarDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<CalendarAccountDto> Accounts { get; init; }
}

/// <summary>Result of the <c>accounts</c> operation: the account list <c>describe</c> carries, as its own
/// narrow round trip. It is what the host reads after the plugin reports a <c>state.update</c> for the
/// kind.</summary>
public sealed record CalendarAccountsResult
{
	public required IReadOnlyList<CalendarAccountDto> Accounts { get; init; }
}

/// <summary>Arguments for the <c>calendars</c> operation. Account ids are config-entry shaped and do not
/// exist at declaration time - see <c>ProviderCapabilityId</c>'s remarks.</summary>
public sealed record CalendarAccountArguments
{
	public required string AccountId { get; init; }
}

/// <summary>Mirrors the SDK's <c>CalendarInfo</c>.</summary>
public sealed record CalendarInfoDto
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	/// <summary><c>#RRGGBB</c>, or null for none.</summary>
	public string? Color { get; init; }

	public bool IsPrimary { get; init; }
}

/// <summary>Result of the <c>calendars</c> operation.</summary>
public sealed record CalendarListResult
{
	public required IReadOnlyList<CalendarInfoDto> Calendars { get; init; }
}

/// <summary>Arguments for the <c>events</c> operation: the SDK's <c>CalendarEventQuery</c> plus the account
/// it addresses.</summary>
public sealed record CalendarEventsArguments
{
	public required string AccountId { get; init; }

	/// <summary>Start of the range, inclusive.</summary>
	public required DateTimeOffset From { get; init; }

	/// <summary>End of the range, exclusive.</summary>
	public required DateTimeOffset To { get; init; }

	/// <summary>Empty means every calendar of the account.</summary>
	public IReadOnlyList<string> CalendarIds { get; init; } = [];
}

/// <summary>
/// One event in an <c>events</c> reply: the SDK's <c>CalendarEvent</c> without its description and
/// participants, which only the <c>event</c> operation carries. Title and location are cut to
/// <c>ProtocolLimits.MaxCalendarTitleLength</c> and <c>ProtocolLimits.MaxCalendarLocationLength</c>.
/// </summary>
public sealed record CalendarEventSummaryDto
{
	public required string Id { get; init; }

	public required string CalendarId { get; init; }

	public required string Title { get; init; }

	public required DateTimeOffset Start { get; init; }

	public required DateTimeOffset End { get; init; }

	public bool IsAllDay { get; init; }

	public string? Location { get; init; }

	public string? MeetingUrl { get; init; }
}

/// <summary>Result of the <c>events</c> operation, ordered by start.</summary>
public sealed record CalendarEventsResult
{
	public required IReadOnlyList<CalendarEventSummaryDto> Events { get; init; }

	/// <summary>True when the latest events were dropped to keep the reply within
	/// <c>ProtocolLimits.MaxCalendarReplyBytes</c>.</summary>
	public bool Truncated { get; init; }
}

/// <summary>Arguments for the <c>event</c> operation.</summary>
public sealed record CalendarEventArguments
{
	public required string AccountId { get; init; }

	public required string CalendarId { get; init; }

	public required string EventId { get; init; }
}

/// <summary>Mirrors the SDK's <c>CalendarParticipant</c>. <see cref="Response" /> is a string, not the
/// SDK's <c>CalendarResponseStatus</c> enum - see <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record CalendarParticipantDto
{
	public string? Name { get; init; }

	public string? Email { get; init; }

	public bool IsOrganizer { get; init; }

	/// <summary>One of the SDK's <c>CalendarResponseStatus</c> member names. A name the reader does not know
	/// reads as <c>Unknown</c>.</summary>
	public string Response { get; init; } = "Unknown";
}

/// <summary>Mirrors the SDK's <c>CalendarEvent</c>, with the description cut to
/// <c>ProtocolLimits.MaxCalendarDescriptionLength</c> and at most
/// <c>ProtocolLimits.MaxCalendarParticipants</c> participants.</summary>
public sealed record CalendarEventDto
{
	public required string Id { get; init; }

	public required string CalendarId { get; init; }

	public required string Title { get; init; }

	public required DateTimeOffset Start { get; init; }

	public required DateTimeOffset End { get; init; }

	public bool IsAllDay { get; init; }

	public string? Location { get; init; }

	public string? Description { get; init; }

	public string? MeetingUrl { get; init; }

	public IReadOnlyList<CalendarParticipantDto> Participants { get; init; } = [];
}

/// <summary>Result of the <c>event</c> operation. <see cref="Event" /> is null when the event no longer
/// exists.</summary>
public sealed record CalendarEventResult
{
	public CalendarEventDto? Event { get; init; }
}
