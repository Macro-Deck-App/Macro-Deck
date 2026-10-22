using System.Text.Json.Serialization;
using MacroDeckHost.Application.Calendar;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class GetIntegrationCalendarsRequest
{
	public string IntegrationId { get; set; } = string.Empty;
}

public class GetIntegrationCalendarsResponse
{
	public bool Found { get; set; }

	public bool Available { get; set; }

	public DateTimeOffset? WindowStart { get; set; }

	public DateTimeOffset? WindowEnd { get; set; }

	public List<IntegrationCalendarAccountDto> Accounts { get; set; } = [];

	public List<IntegrationCalendarEventDto> Events { get; set; } = [];
}

public class IntegrationCalendarAccountDto
{
	public string AccountId { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public CalendarAccountStatus Status { get; set; }

	public List<IntegrationCalendarDto> Calendars { get; set; } = [];
}

public class IntegrationCalendarDto
{
	public string Key { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string? Color { get; set; }

	public bool IsPrimary { get; set; }
}

public class IntegrationCalendarEventDto
{
	public string InstanceKey { get; set; } = string.Empty;

	public string AccountId { get; set; } = string.Empty;

	public string CalendarKey { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public DateTimeOffset Start { get; set; }

	public DateTimeOffset End { get; set; }

	public bool IsAllDay { get; set; }

	public string? Location { get; set; }

	public bool HasMeetingUrl { get; set; }
}
