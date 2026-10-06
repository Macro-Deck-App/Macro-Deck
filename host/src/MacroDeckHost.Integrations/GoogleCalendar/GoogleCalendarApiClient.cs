using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Calendar;

namespace MacroDeckHost.Integrations.GoogleCalendar;

internal sealed partial class GoogleCalendarApiClient : IDisposable
{
	public const string BaseUrl = "https://www.googleapis.com/calendar/v3/";

	private const int MaxPages = 40;

	private static readonly HttpClient _shared = CreateClient(
		new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) },
		disposeHandler: true);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;

	public GoogleCalendarApiClient()
	{
		_http = _shared;
		_ownsClient = false;
	}

	internal GoogleCalendarApiClient(HttpMessageHandler handler)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
	}

	public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(
		GoogleTokenProvider tokens,
		CancellationToken cancellationToken)
	{
		var calendars = new List<CalendarInfo>();

		await foreach (var item in PagedItemsAsync(tokens, "users/me/calendarList?maxResults=250", cancellationToken))
		{
			var id = ReadString(item, "id");
			if (string.IsNullOrEmpty(id) || ReadBool(item, "hidden"))
			{
				continue;
			}

			calendars.Add(new CalendarInfo
			{
				Id = id,
				Name = ReadString(item, "summaryOverride") ?? ReadString(item, "summary") ?? id,
				Color = NormalizeColor(ReadString(item, "backgroundColor")),
				IsPrimary = ReadBool(item, "primary")
			});
		}

		return calendars;
	}

	public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		GoogleTokenProvider tokens,
		string calendarId,
		DateTimeOffset from,
		DateTimeOffset to,
		CancellationToken cancellationToken)
	{
		var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events" +
			$"?timeMin={Uri.EscapeDataString(Rfc3339(from))}" +
			$"&timeMax={Uri.EscapeDataString(Rfc3339(to))}" +
			"&singleEvents=true&orderBy=startTime&maxResults=250";

		var events = new List<CalendarEvent>();
		await foreach (var item in PagedItemsAsync(tokens, path, cancellationToken))
		{
			if (MapEvent(item, calendarId) is { } mapped)
			{
				events.Add(mapped);
			}
		}

		return events;
	}

	public async Task<CalendarEvent?> GetEventAsync(
		GoogleTokenProvider tokens,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
	{
		var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

		using var response = await SendAsync(tokens, path, cancellationToken);
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

	private static CalendarEvent? MapEvent(JsonElement item, string calendarId)
	{
		var id = ReadString(item, "id");
		if (string.IsNullOrEmpty(id) ||
			string.Equals(ReadString(item, "status"), "cancelled", StringComparison.Ordinal) ||
			ReadTime(item, "start") is not { } start ||
			ReadTime(item, "end") is not { } end)
		{
			return null;
		}

		return new CalendarEvent
		{
			Id = id,
			CalendarId = calendarId,
			Title = ReadString(item, "summary") ?? string.Empty,
			Start = start.Value,
			End = end.Value,
			IsAllDay = start.IsDate,
			Location = ReadString(item, "location"),
			Description = ReadString(item, "description"),
			MeetingUrl = ReadMeetingUrl(item),
			Participants = ReadParticipants(item)
		};
	}

	private async IAsyncEnumerable<JsonElement> PagedItemsAsync(
		GoogleTokenProvider tokens,
		string path,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		string? pageToken = null;
		for (var page = 0; page < MaxPages; page++)
		{
			var pagePath = pageToken is null ? path : $"{path}&pageToken={Uri.EscapeDataString(pageToken)}";

			using var response = await SendAsync(tokens, pagePath, cancellationToken);
			using var document = await ReadSuccessAsync(response, cancellationToken);
			var root = document.RootElement;

			if (root.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Array)
			{
				foreach (var item in items.EnumerateArray())
				{
					yield return item.Clone();
				}
			}

			var next = ReadString(root, "nextPageToken");
			if (string.IsNullOrEmpty(next) || next == pageToken)
			{
				yield break;
			}

			pageToken = next;
		}
	}

	private async Task<HttpResponseMessage> SendAsync(
		GoogleTokenProvider tokens,
		string path,
		CancellationToken cancellationToken)
	{
		var accessToken = await tokens.GetAsync(cancellationToken);
		var response = await SendOnceAsync(path, accessToken, cancellationToken);
		if (response.StatusCode is not HttpStatusCode.Unauthorized)
		{
			return response;
		}

		response.Dispose();
		var refreshed = await tokens.ForceRefreshAsync(accessToken, cancellationToken);
		return await SendOnceAsync(path, refreshed, cancellationToken);
	}

	private async Task<HttpResponseMessage> SendOnceAsync(
		string path,
		string accessToken,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException("The request to Google Calendar timed out.", ex);
		}
	}

	private static async Task<JsonDocument> ReadSuccessAsync(
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Google Calendar answered {(int)response.StatusCode} {response.ReasonPhrase}.",
				null,
				response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		return JsonDocument.Parse(body);
	}

	private static (DateTimeOffset Value, bool IsDate)? ReadTime(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var time) || time.ValueKind is not JsonValueKind.Object)
		{
			return null;
		}

		if (ReadString(time, "dateTime") is { } dateTime &&
			DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			return (parsed, false);
		}

		if (ReadString(time, "date") is { } date &&
			DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
		{
			return (new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), true);
		}

		return null;
	}

	private static string? ReadMeetingUrl(JsonElement item)
	{
		if (item.TryGetProperty("conferenceData", out var conference) &&
			conference.ValueKind is JsonValueKind.Object &&
			conference.TryGetProperty("entryPoints", out var entryPoints) &&
			entryPoints.ValueKind is JsonValueKind.Array)
		{
			foreach (var entryPoint in entryPoints.EnumerateArray())
			{
				if (string.Equals(ReadString(entryPoint, "entryPointType"), "video", StringComparison.Ordinal) &&
					ReadString(entryPoint, "uri") is { Length: > 0 } uri)
				{
					return uri;
				}
			}
		}

		return ReadString(item, "hangoutLink") is { Length: > 0 } hangout ? hangout : null;
	}

	private static List<CalendarParticipant> ReadParticipants(JsonElement item)
	{
		if (!item.TryGetProperty("attendees", out var attendees) || attendees.ValueKind is not JsonValueKind.Array)
		{
			return [];
		}

		var participants = new List<CalendarParticipant>();
		foreach (var attendee in attendees.EnumerateArray())
		{
			if (ReadBool(attendee, "resource"))
			{
				continue;
			}

			var name = ReadString(attendee, "displayName");
			var email = ReadString(attendee, "email");
			if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(email))
			{
				continue;
			}

			participants.Add(new CalendarParticipant
			{
				Name = name,
				Email = email,
				IsOrganizer = ReadBool(attendee, "organizer"),
				Response = ReadString(attendee, "responseStatus") switch
				{
					"needsAction" => CalendarResponseStatus.NeedsAction,
					"accepted" => CalendarResponseStatus.Accepted,
					"declined" => CalendarResponseStatus.Declined,
					"tentative" => CalendarResponseStatus.Tentative,
					_ => CalendarResponseStatus.Unknown
				}
			});
		}

		return participants;
	}

	private static string? NormalizeColor(string? color)
		=> color is not null && HexColor().IsMatch(color) ? color.ToUpperInvariant() : null;

	private static string Rfc3339(DateTimeOffset value)
		=> value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

	private static string? ReadString(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
			? value.GetString()
			: null;

	private static bool ReadBool(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

	private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
		=> new(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(20) };

	[GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
	private static partial Regex HexColor();
}
