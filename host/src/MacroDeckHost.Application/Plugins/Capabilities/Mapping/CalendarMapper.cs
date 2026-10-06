using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Calendar;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

// Every text a plugin sends is clipped again here: the plugin is untrusted, and the SDK's own clipping
// only binds a plugin built against it.
public static class CalendarMapper
{
	public static CalendarAccount ToDomain(CalendarAccountDto dto)
		=> new()
		{
			Id = dto.Id,
			DisplayName = CalendarText.Clip(dto.DisplayName, ProtocolLimits.MaxCalendarTitleLength)
		};

	public static CalendarAccountDto ToDto(CalendarAccount account)
		=> new() { Id = account.Id, DisplayName = account.DisplayName };

	public static CalendarInfo ToDomain(CalendarInfoDto dto)
		=> new()
		{
			Id = dto.Id,
			Name = CalendarText.Clip(dto.Name, ProtocolLimits.MaxCalendarTitleLength),
			Color = dto.Color,
			IsPrimary = dto.IsPrimary
		};

	public static CalendarEvent ToDomain(CalendarEventSummaryDto dto)
		=> new()
		{
			Id = dto.Id,
			CalendarId = dto.CalendarId,
			Title = CalendarText.Clip(dto.Title, ProtocolLimits.MaxCalendarTitleLength),
			Start = dto.Start,
			End = dto.End,
			IsAllDay = dto.IsAllDay,
			Location = CalendarText.ClipOptional(dto.Location, ProtocolLimits.MaxCalendarLocationLength),
			MeetingUrl = MeetingUrl(dto.MeetingUrl)
		};

	public static CalendarEvent ToDomain(CalendarEventDto dto)
		=> new()
		{
			Id = dto.Id,
			CalendarId = dto.CalendarId,
			Title = CalendarText.Clip(dto.Title, ProtocolLimits.MaxCalendarTitleLength),
			Start = dto.Start,
			End = dto.End,
			IsAllDay = dto.IsAllDay,
			Location = CalendarText.ClipOptional(dto.Location, ProtocolLimits.MaxCalendarLocationLength),
			Description = CalendarText.ClipOptional(dto.Description, ProtocolLimits.MaxCalendarDescriptionLength),
			MeetingUrl = MeetingUrl(dto.MeetingUrl),
			Participants = [.. dto.Participants.Take(ProtocolLimits.MaxCalendarParticipants).Select(ToDomain)]
		};

	private static CalendarParticipant ToDomain(CalendarParticipantDto dto)
		=> new()
		{
			Name = CalendarText.ClipOptional(dto.Name, ProtocolLimits.MaxCalendarTitleLength),
			Email = CalendarText.ClipOptional(dto.Email, ProtocolLimits.MaxCalendarTitleLength),
			IsOrganizer = dto.IsOrganizer,
			Response = ToResponseStatus(dto.Response)
		};

	// Matched by name rather than through Enum.TryParse, which also accepts "3" and a comma list.
	private static CalendarResponseStatus ToResponseStatus(string? response) => response switch
	{
		nameof(CalendarResponseStatus.NeedsAction) => CalendarResponseStatus.NeedsAction,
		nameof(CalendarResponseStatus.Accepted) => CalendarResponseStatus.Accepted,
		nameof(CalendarResponseStatus.Declined) => CalendarResponseStatus.Declined,
		nameof(CalendarResponseStatus.Tentative) => CalendarResponseStatus.Tentative,
		_ => CalendarResponseStatus.Unknown
	};

	private static string? MeetingUrl(string? url)
		=> url is { Length: > 0 and <= ProtocolLimits.MaxCalendarMeetingUrlLength } ? url : null;
}
