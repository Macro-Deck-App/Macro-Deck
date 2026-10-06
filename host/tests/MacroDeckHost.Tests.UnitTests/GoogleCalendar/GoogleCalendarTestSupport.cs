using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.GoogleCalendar;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Spotify;
using RecordingIntegrationConfig = MacroDeckHost.Tests.UnitTests.Twitch.RecordingIntegrationConfig;

namespace MacroDeckHost.Tests.UnitTests.GoogleCalendar;

internal sealed class GoogleRecordedRequest(HttpMethod method, Uri uri, string? authorization, string body)
{
	public HttpMethod Method { get; } = method;

	public Uri Uri { get; } = uri;

	public string? Authorization { get; } = authorization;

	public string Body { get; } = body;

	public string PathAndQuery => Uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);

	public IReadOnlyDictionary<string, string> Form => ParseQuery(Body);

	public IReadOnlyDictionary<string, string> Query => ParseQuery(Uri.Query.TrimStart('?'));

	private static Dictionary<string, string> ParseQuery(string query)
		=> query.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
				pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
				StringComparer.Ordinal);
}

internal sealed class GoogleFakeServer : HttpMessageHandler
{
	private readonly List<(Func<GoogleRecordedRequest, bool> Match, Func<GoogleRecordedRequest, (HttpStatusCode, string)> Reply)>
		_routes = [];

	public List<GoogleRecordedRequest> Requests { get; } = [];

	public GoogleFakeServer On(Func<GoogleRecordedRequest, bool> match, HttpStatusCode status, string body)
		=> On(match, _ => (status, body));

	public GoogleFakeServer On(
		Func<GoogleRecordedRequest, bool> match,
		Func<GoogleRecordedRequest, (HttpStatusCode, string)> reply)
	{
		_routes.Insert(0, (match, reply));
		return this;
	}

	public IReadOnlyList<GoogleRecordedRequest> To(string pathPrefix)
		=> Requests.Where(r => r.Uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal)).ToList();

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		var recorded = new GoogleRecordedRequest(request.Method,
			request.RequestUri!,
			request.Headers.Authorization?.ToString(),
			request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
		Requests.Add(recorded);

		var route = _routes.FirstOrDefault(r => r.Match(recorded));
		var (status, body) = route.Reply is null ? (HttpStatusCode.NotFound, "{}") : route.Reply(recorded);
		return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}
}

internal sealed class FakeConfigFlowContext : IConfigFlowContext, IOAuthSession
{
	public IOAuthSession OAuth => this;

	public string RedirectUri { get; init; } = "http://127.0.0.1:8191/api/integrations/oauth/callback";

	public string State { get; init; } = "state-123";

	public string? AuthorizationCode { get; set; }
}

internal sealed class GoogleCalendarHarness : IDisposable
{
	public const string TokenPath = "/token";
	public const string CalendarListPath = "/calendar/v3/users/me/calendarList";

	public GoogleCalendarHarness()
	{
		Integration = new GoogleCalendarIntegration(() => new GoogleOAuthClient(Server),
			new GoogleCalendarApiClient(Server),
			Time);
	}

	public GoogleFakeServer Server { get; } = new();

	public ManualTimeProvider Time { get; } = new();

	public RecordingIntegrationConfig Config { get; } = new();

	public GoogleCalendarIntegration Integration { get; }

	public Guid AddAccount(
		string email,
		TimeSpan? expiresIn = null,
		DateTimeOffset? connectedAt = null,
		string? title = null,
		string accessToken = "access-0",
		string refreshToken = "refresh-0")
		=> Config.AddEntry(title ?? email,
			new Dictionary<string, string?>
			{
				[GoogleCalendarConfigKeys.ClientId] = "client-id",
				[GoogleCalendarConfigKeys.Email] = email,
				[GoogleCalendarConfigKeys.ExpiresAt] = (Time.Now + (expiresIn ?? TimeSpan.FromHours(1)))
					.ToString("o", CultureInfo.InvariantCulture),
				[GoogleCalendarConfigKeys.ConnectedAt] = (connectedAt ?? Time.Now).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>
			{
				[GoogleCalendarConfigKeys.ClientSecret] = "client-secret",
				[GoogleCalendarConfigKeys.AccessToken] = accessToken,
				[GoogleCalendarConfigKeys.RefreshToken] = refreshToken
			});

	public Task StartAsync() => Integration.InitializeAsync(new SpotifyContextStub(Config));

	public GoogleCalendarHarness Calendars(params string[] items)
	{
		Server.On(r => r.Uri.AbsolutePath == CalendarListPath,
			HttpStatusCode.OK,
			$$"""{"items":[{{string.Join(',', items)}}]}""");
		return this;
	}

	public GoogleCalendarHarness Events(string calendarId, params string[] items)
	{
		Server.On(r => r.Uri.AbsolutePath == EventsPath(calendarId) && !r.Query.ContainsKey("pageToken"),
			HttpStatusCode.OK,
			$$"""{"items":[{{string.Join(',', items)}}]}""");
		return this;
	}

	public static string EventsPath(string calendarId)
		=> $"/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events";

	public static string IdToken(string email)
		=> $"{Segment("""{"alg":"RS256"}""")}.{Segment($$"""{"email":"{{email}}","sub":"1"}""")}.signature";

	private static string Segment(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

	public void Dispose()
	{
		Integration.Dispose();
		Server.Dispose();
	}
}
