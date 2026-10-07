using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MacroDeckHost.Integrations.MicrosoftCalendar;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Spotify;
using RecordingIntegrationConfig = MacroDeckHost.Tests.UnitTests.Twitch.RecordingIntegrationConfig;

namespace MacroDeckHost.Tests.UnitTests.MicrosoftCalendar;

internal sealed class GraphRecordedRequest(
	HttpMethod method,
	Uri uri,
	string? authorization,
	IReadOnlyList<string> prefer,
	string body)
{
	public HttpMethod Method { get; } = method;

	public Uri Uri { get; } = uri;

	public string? Authorization { get; } = authorization;

	public IReadOnlyList<string> Prefer { get; } = prefer;

	public string Body { get; } = body;

	public IReadOnlyDictionary<string, string> Form => ParseQuery(Body);

	public IReadOnlyDictionary<string, string> Query => ParseQuery(Uri.Query.TrimStart('?'));

	public static IReadOnlyDictionary<string, string> ParseQuery(string query)
		=> query.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
				pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
				StringComparer.Ordinal);
}

internal sealed record GraphReply(HttpStatusCode Status, string Body, TimeSpan? RetryAfter = null);

internal sealed class GraphFakeServer : HttpMessageHandler
{
	private readonly List<(Func<GraphRecordedRequest, bool> Match, Func<GraphRecordedRequest, GraphReply> Reply)>
		_routes = [];

	public List<GraphRecordedRequest> Requests { get; } = [];

	public GraphFakeServer On(Func<GraphRecordedRequest, bool> match, HttpStatusCode status, string body)
		=> On(match, _ => new GraphReply(status, body));

	public GraphFakeServer On(Func<GraphRecordedRequest, bool> match, Func<GraphRecordedRequest, GraphReply> reply)
	{
		_routes.Insert(0, (match, reply));
		return this;
	}

	public IReadOnlyList<GraphRecordedRequest> To(string pathPrefix)
		=> Requests.Where(r => r.Uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal)).ToList();

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		var recorded = new GraphRecordedRequest(request.Method,
			request.RequestUri!,
			request.Headers.Authorization?.ToString(),
			request.Headers.TryGetValues("Prefer", out var prefer) ? prefer.ToList() : [],
			request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
		Requests.Add(recorded);

		var route = _routes.FirstOrDefault(r => r.Match(recorded));
		var reply = route.Reply is null ? new GraphReply(HttpStatusCode.NotFound, "{}") : route.Reply(recorded);
		var response = new HttpResponseMessage(reply.Status)
		{
			Content = new StringContent(reply.Body, Encoding.UTF8, "application/json")
		};
		if (reply.RetryAfter is { } retryAfter)
		{
			response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
		}

		return response;
	}
}

internal sealed class MicrosoftCalendarHarness : IDisposable
{
	public const string TokenPath = "/common/oauth2/v2.0/token";
	public const string CalendarsPath = "/v1.0/me/calendars";
	public const string ClientId = "11111111-2222-3333-4444-555555555555";

	public MicrosoftCalendarHarness()
	{
		Integration = new MicrosoftCalendarIntegration(() => new MicrosoftOAuthClient(Server),
			new MicrosoftGraphClient(Server, Time),
			Time);
	}

	public GraphFakeServer Server { get; } = new();

	public ManualTimeProvider Time { get; } = new();

	public RecordingIntegrationConfig Config { get; } = new();

	public MicrosoftCalendarIntegration Integration { get; }

	public Guid AddAccount(
		string email,
		string[]? calendarIds = null,
		TimeSpan? expiresIn = null,
		DateTimeOffset? connectedAt = null,
		string? title = null,
		string? accountKey = null,
		string accessToken = "access-0",
		string refreshToken = "refresh-0")
		=> Config.AddEntry(title ?? email,
			new Dictionary<string, string?>
			{
				[MicrosoftCalendarConfigKeys.ClientId] = ClientId,
				[MicrosoftCalendarConfigKeys.Email] = email,
				[MicrosoftCalendarConfigKeys.AccountKey] = accountKey ?? $"tenant/{email}",
				[MicrosoftCalendarConfigKeys.CalendarIds]
					= "[" + string.Join(',', (calendarIds ?? ["cal-1"]).Select(id => $"\"{id}\"")) + "]",
				[MicrosoftCalendarConfigKeys.ExpiresAt] = (Time.Now + (expiresIn ?? TimeSpan.FromHours(1)))
					.ToString("o", CultureInfo.InvariantCulture),
				[MicrosoftCalendarConfigKeys.ConnectedAt]
					= (connectedAt ?? Time.Now).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>
			{
				[MicrosoftCalendarConfigKeys.AccessToken] = accessToken,
				[MicrosoftCalendarConfigKeys.RefreshToken] = refreshToken
			});

	public Task StartAsync() => Integration.InitializeAsync(new SpotifyContextStub(Config));

	public MicrosoftCalendarHarness Calendars(params string[] items)
	{
		Server.On(r => r.Uri.AbsolutePath == CalendarsPath,
			HttpStatusCode.OK,
			$$"""{"@odata.context":"https://graph.microsoft.com/v1.0/$metadata#me/calendars","value":[{{string.Join(',', items)}}]}""");
		return this;
	}

	public MicrosoftCalendarHarness Events(string calendarId, params string[] items)
	{
		Server.On(r => r.Uri.AbsolutePath == CalendarViewPath(calendarId) && !r.Query.ContainsKey("$skiptoken"),
			HttpStatusCode.OK,
			$$"""{"value":[{{string.Join(',', items)}}]}""");
		return this;
	}

	public static string Calendar(
		string id,
		string name,
		string ownerAddress = "me@contoso.com",
		string ownerName = "Megan Bowen",
		bool isDefault = false,
		string hexColor = "#e3008c")
		=> $$"""
			{"id":"{{id}}","name":"{{name}}","color":"auto","hexColor":"{{hexColor}}","isDefaultCalendar":{{(isDefault ? "true" : "false")}},"canEdit":true,"owner":{"name":"{{ownerName}}","address":"{{ownerAddress}}"} }
			""";

	public static string CalendarViewPath(string calendarId) => $"/v1.0/me/calendars/{calendarId}/calendarView";

	public static string EventPath(string calendarId, string eventId) => $"/v1.0/me/calendars/{calendarId}/events/{eventId}";

	public static string IdToken(string email, string oid = "oid-1", string tid = "tid-1", string name = "Megan Bowen")
		=> $"{Segment("""{"typ":"JWT","alg":"RS256"}""")}." +
			$"{Segment($$"""{"oid":"{{oid}}","tid":"{{tid}}","preferred_username":"{{email}}","name":"{{name}}"}""")}.signature";

	private static string Segment(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

	public void Dispose()
	{
		Integration.Dispose();
		Server.Dispose();
	}
}
