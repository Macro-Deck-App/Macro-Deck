using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Integrations.Http;

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

internal sealed record MicrosoftCalendarSummary(
	string Id,
	string Name,
	string? Color,
	bool IsDefault,
	string? OwnerName,
	string? OwnerAddress);

internal sealed class MicrosoftGraphThrottledException(DateTimeOffset retryAt)
	: HttpRequestException("Microsoft Graph asked to slow down.", null, HttpStatusCode.TooManyRequests)
{
	public DateTimeOffset RetryAt { get; } = retryAt;
}

internal sealed partial class MicrosoftGraphClient : IDisposable
{
	public const string BaseUrl = "https://graph.microsoft.com/v1.0/";

	private const int MaxPages = 40;

	private const string ListFields
		= "id,subject,start,end,isAllDay,isCancelled,location,organizer,attendees,onlineMeeting,onlineMeetingUrl," +
		"originalStartTimeZone";

	private static readonly TimeSpan _defaultRetryAfter = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _maxRetryAfter = TimeSpan.FromMinutes(10);

	private static readonly HttpClient _shared = CreateClient(
		IntegrationHttp.CreateHandler(handler => handler.PooledConnectionLifetime = TimeSpan.FromMinutes(10)),
		disposeHandler: true);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly TimeProvider _time;

	public MicrosoftGraphClient(TimeProvider time)
	{
		_http = _shared;
		_ownsClient = false;
		_time = time;
	}

	internal MicrosoftGraphClient(HttpMessageHandler handler, TimeProvider time)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
		_time = time;
	}

	public async Task<IReadOnlyList<MicrosoftCalendarSummary>> GetCalendarsAsync(
		MicrosoftTokenProvider tokens,
		MicrosoftCalendarAccount? account,
		CancellationToken cancellationToken)
	{
		var calendars = new List<MicrosoftCalendarSummary>();
		await foreach (var item in PagedItemsAsync(tokens,
			account,
			"me/calendars?$top=100&$select=id,name,hexColor,isDefaultCalendar,owner",
			cancellationToken))
		{
			var id = ReadString(item, "id");
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}

			var owner = item.TryGetProperty("owner", out var ownerElement) &&
				ownerElement.ValueKind is JsonValueKind.Object
					? ownerElement
					: default;
			calendars.Add(new MicrosoftCalendarSummary(id,
				ReadString(item, "name") is { Length: > 0 } name ? name : id,
				NormalizeColor(ReadString(item, "hexColor")),
				ReadBool(item, "isDefaultCalendar"),
				owner.ValueKind is JsonValueKind.Object ? ReadString(owner, "name") : null,
				owner.ValueKind is JsonValueKind.Object ? ReadString(owner, "address") : null));
		}

		return calendars;
	}

	public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		MicrosoftCalendarAccount account,
		string calendarId,
		DateTimeOffset from,
		DateTimeOffset to,
		CancellationToken cancellationToken)
	{
		var path = $"me/calendars/{Uri.EscapeDataString(calendarId)}/calendarView" +
			$"?startDateTime={Uri.EscapeDataString(Iso(from))}" +
			$"&endDateTime={Uri.EscapeDataString(Iso(to))}" +
			$"&$top=250&$select={ListFields}";

		var events = new List<CalendarEvent>();
		await foreach (var item in PagedItemsAsync(account.Tokens, account, path, cancellationToken))
		{
			if (MapEvent(item, calendarId) is { } mapped)
			{
				events.Add(mapped);
			}
		}

		return events;
	}

	public async Task<CalendarEvent?> GetEventAsync(
		MicrosoftCalendarAccount account,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
	{
		var path = $"me/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}" +
			$"?$select={ListFields},body";

		using var response = await SendAsync(account.Tokens, account, BaseUrl + path, cancellationToken);
		if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
		{
			return null;
		}

		using var document = await ReadSuccessAsync(response, cancellationToken);
		return MapEvent(document.RootElement, calendarId);
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	internal static CalendarEvent? MapEvent(JsonElement item, string calendarId)
	{
		var id = ReadString(item, "id");
		if (string.IsNullOrEmpty(id) ||
			ReadBool(item, "isCancelled") ||
			ReadTime(item, "start") is not { } start ||
			ReadTime(item, "end") is not { } end)
		{
			return null;
		}

		var isAllDay = ReadBool(item, "isAllDay");
		if (isAllDay)
		{
			var zone = FindZone(ReadString(item, "originalStartTimeZone"));
			start = AllDayDate(start, zone);
			end = AllDayDate(end, zone);
		}

		return new CalendarEvent
		{
			Id = id,
			CalendarId = calendarId,
			Title = ReadString(item, "subject") ?? string.Empty,
			Start = start,
			End = end < start ? start : end,
			IsAllDay = isAllDay,
			Location = ReadLocation(item),
			Description = ReadDescription(item),
			MeetingUrl = ReadMeetingUrl(item),
			Participants = ReadParticipants(item)
		};
	}

	private async IAsyncEnumerable<JsonElement> PagedItemsAsync(
		MicrosoftTokenProvider tokens,
		MicrosoftCalendarAccount? account,
		string path,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var url = BaseUrl + path;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (var page = 0; page < MaxPages && seen.Add(url); page++)
		{
			using var response = await SendAsync(tokens, account, url, cancellationToken);
			using var document = await ReadSuccessAsync(response, cancellationToken);
			var root = document.RootElement;

			if (root.TryGetProperty("value", out var items) && items.ValueKind is JsonValueKind.Array)
			{
				foreach (var item in items.EnumerateArray())
				{
					yield return item.Clone();
				}
			}

			// The next link carries the bearer token on the next request, so it may only point back at Graph.
			var next = ReadString(root, "@odata.nextLink");
			if (string.IsNullOrEmpty(next) || !next.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
			{
				yield break;
			}

			url = next;
		}
	}

	private async Task<HttpResponseMessage> SendAsync(
		MicrosoftTokenProvider tokens,
		MicrosoftCalendarAccount? account,
		string url,
		CancellationToken cancellationToken)
	{
		if (account is not null && account.ThrottledUntil > _time.GetUtcNow())
		{
			throw new MicrosoftGraphThrottledException(account.ThrottledUntil);
		}

		var accessToken = await tokens.GetAsync(cancellationToken);
		var response = await SendOnceAsync(url, accessToken, cancellationToken);
		if (response.StatusCode is HttpStatusCode.Unauthorized)
		{
			response.Dispose();
			var refreshed = await tokens.ForceRefreshAsync(accessToken, cancellationToken);
			response = await SendOnceAsync(url, refreshed, cancellationToken);
		}

		if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
			or HttpStatusCode.GatewayTimeout)
		{
			var retryAt = _time.GetUtcNow() + RetryAfter(response);
			response.Dispose();
			if (account is not null)
			{
				account.ThrottledUntil = retryAt;
			}

			throw new MicrosoftGraphThrottledException(retryAt);
		}

		return response;
	}

	private async Task<HttpResponseMessage> SendOnceAsync(
		string url,
		string accessToken,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
		request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");
		request.Headers.TryAddWithoutValidation("Prefer", "IdType=\"ImmutableId\"");
		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException("The request to Microsoft Graph timed out.", ex);
		}
	}

	private TimeSpan RetryAfter(HttpResponseMessage response)
	{
		var retryAfter = response.Headers.RetryAfter;
		var wait = retryAfter?.Delta ??
			(retryAfter?.Date is { } date ? date - _time.GetUtcNow() : _defaultRetryAfter);
		return wait <= TimeSpan.Zero ? _defaultRetryAfter : wait > _maxRetryAfter ? _maxRetryAfter : wait;
	}

	private static async Task<JsonDocument> ReadSuccessAsync(
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Microsoft Graph answered {(int)response.StatusCode} {response.ReasonPhrase}.",
				null,
				response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		return JsonDocument.Parse(body);
	}

	private static DateTimeOffset? ReadTime(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var time) ||
			time.ValueKind is not JsonValueKind.Object ||
			ReadString(time, "dateTime") is not { } dateTime ||
			!DateTime.TryParse(dateTime,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
				out var parsed))
		{
			return null;
		}

		var zoneName = ReadString(time, "timeZone");
		if (zoneName is null || string.Equals(zoneName, "UTC", StringComparison.OrdinalIgnoreCase) ||
			FindZone(zoneName) is not { } zone)
		{
			return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
		}

		var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
		return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
	}

	// Graph may return an all-day boundary as a floating midnight or as that midnight converted to
	// UTC from the event's own zone; only the calendar date counts either way.
	private static DateTimeOffset AllDayDate(DateTimeOffset utc, TimeZoneInfo? zone)
	{
		DateTime date;
		if (utc.UtcDateTime.TimeOfDay == TimeSpan.Zero)
		{
			date = utc.UtcDateTime.Date;
		}
		else if (zone is not null && TimeZoneInfo.ConvertTime(utc, zone) is { TimeOfDay.Ticks: 0 } local)
		{
			date = local.Date;
		}
		else
		{
			date = utc.UtcDateTime.AddHours(12).Date;
		}

		return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.Zero);
	}

	private static TimeZoneInfo? FindZone(string? id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(id);
		}
		catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) &&
				TimeZoneInfo.TryFindSystemTimeZoneById(iana, out var zone)
					? zone
					: null;
		}
	}

	private static string? ReadLocation(JsonElement item)
		=> item.TryGetProperty("location", out var location) && location.ValueKind is JsonValueKind.Object &&
			ReadString(location, "displayName") is { Length: > 0 } name
				? name
				: null;

	private static string? ReadDescription(JsonElement item)
		=> item.TryGetProperty("body", out var body) && body.ValueKind is JsonValueKind.Object &&
			ReadString(body, "content") is { } content &&
			content.Trim() is { Length: > 0 } trimmed
				? trimmed
				: null;

	private static string? ReadMeetingUrl(JsonElement item)
	{
		if (item.TryGetProperty("onlineMeeting", out var meeting) &&
			meeting.ValueKind is JsonValueKind.Object &&
			ReadString(meeting, "joinUrl") is { Length: > 0 } joinUrl)
		{
			return joinUrl;
		}

		return ReadString(item, "onlineMeetingUrl") is { Length: > 0 } url ? url : null;
	}

	private static List<CalendarParticipant> ReadParticipants(JsonElement item)
	{
		var participants = new List<CalendarParticipant>();
		var organizerEmail = default(string);

		if (item.TryGetProperty("organizer", out var organizer) &&
			ReadEmailAddress(organizer) is { } organizerAddress)
		{
			organizerEmail = organizerAddress.Email;
			participants.Add(new CalendarParticipant
			{
				Name = organizerAddress.Name,
				Email = organizerAddress.Email,
				IsOrganizer = true,
				Response = CalendarResponseStatus.Accepted
			});
		}

		if (!item.TryGetProperty("attendees", out var attendees) || attendees.ValueKind is not JsonValueKind.Array)
		{
			return participants;
		}

		foreach (var attendee in attendees.EnumerateArray())
		{
			if (string.Equals(ReadString(attendee, "type"), "resource", StringComparison.OrdinalIgnoreCase) ||
				ReadEmailAddress(attendee) is not { } address)
			{
				continue;
			}

			if (address.Email is not null &&
				string.Equals(address.Email, organizerEmail, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var response = attendee.TryGetProperty("status", out var status) && status.ValueKind is JsonValueKind.Object
				? ReadString(status, "response")
				: null;
			participants.Add(new CalendarParticipant
			{
				Name = address.Name,
				Email = address.Email,
				IsOrganizer = string.Equals(response, "organizer", StringComparison.OrdinalIgnoreCase),
				Response = response?.ToLowerInvariant() switch
				{
					"none" or "notresponded" => CalendarResponseStatus.NeedsAction,
					"organizer" or "accepted" => CalendarResponseStatus.Accepted,
					"tentativelyaccepted" => CalendarResponseStatus.Tentative,
					"declined" => CalendarResponseStatus.Declined,
					_ => CalendarResponseStatus.Unknown
				}
			});
		}

		return participants;
	}

	private static (string? Name, string? Email)? ReadEmailAddress(JsonElement recipient)
	{
		if (recipient.ValueKind is not JsonValueKind.Object ||
			!recipient.TryGetProperty("emailAddress", out var address) ||
			address.ValueKind is not JsonValueKind.Object)
		{
			return null;
		}

		var name = ReadString(address, "name");
		var email = ReadString(address, "address");
		return string.IsNullOrEmpty(name) && string.IsNullOrEmpty(email)
			? null
			: (string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(email) ? null : email);
	}

	private static string? NormalizeColor(string? color)
		=> color is not null && HexColor().IsMatch(color) ? color.ToUpperInvariant() : null;

	private static string Iso(DateTimeOffset value)
		=> value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

	private static string? ReadString(JsonElement root, string name)
		=> root.ValueKind is JsonValueKind.Object &&
			root.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.String
				? value.GetString()
				: null;

	private static bool ReadBool(JsonElement root, string name)
		=> root.ValueKind is JsonValueKind.Object &&
			root.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.True;

	private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
		=> new(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(20) };

	[GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
	private static partial Regex HexColor();
}
