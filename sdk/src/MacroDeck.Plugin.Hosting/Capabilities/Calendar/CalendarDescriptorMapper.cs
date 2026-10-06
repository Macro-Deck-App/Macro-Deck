using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Calendar;

namespace MacroDeck.Plugin.Hosting.Capabilities.Calendar;

internal static class CalendarDescriptorMapper
{
	private const int ResultFramingBytes = 64;

	public static CalendarAccountDto ToDto(CalendarAccount account)
		=> new() { Id = account.Id, DisplayName = Clip(account.DisplayName, ProtocolLimits.MaxCalendarTitleLength) };

	public static CalendarInfoDto ToDto(CalendarInfo calendar)
		=> new()
		{
			Id = calendar.Id,
			Name = Clip(calendar.Name, ProtocolLimits.MaxCalendarTitleLength),
			Color = calendar.Color,
			IsPrimary = calendar.IsPrimary
		};

	public static CalendarEventsResult ToEventsResult(IEnumerable<CalendarEvent> events)
	{
		var budget = ProtocolLimits.MaxCalendarReplyBytes - ResultFramingBytes;
		var used = 0;
		var summaries = new List<CalendarEventSummaryDto>();
		var truncated = false;

		foreach (var calendarEvent in events.OrderBy(e => e.Start).ThenBy(e => e.End))
		{
			var summary = ToSummary(calendarEvent);
			var size = SerializedSize(summary) + 1;

			if (used + size > budget)
			{
				truncated = true;
				break;
			}

			used += size;
			summaries.Add(summary);
		}

		return new CalendarEventsResult { Events = summaries, Truncated = truncated };
	}

	public static CalendarEventDto ToDto(CalendarEvent calendarEvent)
	{
		var dto = new CalendarEventDto
		{
			Id = calendarEvent.Id,
			CalendarId = calendarEvent.CalendarId,
			Title = Clip(calendarEvent.Title, ProtocolLimits.MaxCalendarTitleLength),
			Start = calendarEvent.Start,
			End = calendarEvent.End,
			IsAllDay = calendarEvent.IsAllDay,
			Location = ClipOptional(calendarEvent.Location, ProtocolLimits.MaxCalendarLocationLength),
			Description = ClipOptional(calendarEvent.Description, ProtocolLimits.MaxCalendarDescriptionLength),
			MeetingUrl = MeetingUrl(calendarEvent.MeetingUrl),
			Participants =
			[
				.. calendarEvent.Participants.Take(ProtocolLimits.MaxCalendarParticipants).Select(ToDto)
			]
		};

		var budget = ProtocolLimits.MaxCalendarReplyBytes - ResultFramingBytes;
		while (dto.Participants.Count > 0 && SerializedSize(dto) > budget)
		{
			dto = dto with { Participants = [.. dto.Participants.Take(dto.Participants.Count / 2)] };
		}

		return SerializedSize(dto) > budget ? dto with { Description = null } : dto;
	}

	private static CalendarEventSummaryDto ToSummary(CalendarEvent calendarEvent)
		=> new()
		{
			Id = calendarEvent.Id,
			CalendarId = calendarEvent.CalendarId,
			Title = Clip(calendarEvent.Title, ProtocolLimits.MaxCalendarTitleLength),
			Start = calendarEvent.Start,
			End = calendarEvent.End,
			IsAllDay = calendarEvent.IsAllDay,
			Location = ClipOptional(calendarEvent.Location, ProtocolLimits.MaxCalendarLocationLength),
			MeetingUrl = MeetingUrl(calendarEvent.MeetingUrl)
		};

	private static CalendarParticipantDto ToDto(CalendarParticipant participant)
		=> new()
		{
			Name = ClipOptional(participant.Name, ProtocolLimits.MaxCalendarTitleLength),
			Email = ClipOptional(participant.Email, ProtocolLimits.MaxCalendarTitleLength),
			IsOrganizer = participant.IsOrganizer,
			Response = Enum.IsDefined(participant.Response)
				? participant.Response.ToString()
				: nameof(CalendarResponseStatus.Unknown)
		};

	private static string? MeetingUrl(string? url)
		=> url is { Length: > 0 and <= ProtocolLimits.MaxCalendarMeetingUrlLength } ? url : null;

	private static int SerializedSize<T>(T value)
		=> JsonSerializer.SerializeToUtf8Bytes(value, PluginProtocolJson.Options).Length;

	private static string? ClipOptional(string? text, int maxLength) => text is null ? null : Clip(text, maxLength);

	private static string Clip(string text, int maxLength)
	{
		if (text.Length <= maxLength)
		{
			return text;
		}

		var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
		return text[..length];
	}
}
