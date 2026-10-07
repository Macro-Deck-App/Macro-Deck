using System.Net;
using System.Text;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeClientTests
{
	private const string StatusJson =
		"""{"protection_enabled":true,"protection_disabled_duration":0,"running":true,"version":"v0.107.57","dns_port":53}""";

	[Test]
	public async Task Credentials_are_sent_as_basic_auth()
	{
		var handler = new RoutingHandler { ["GET /control/status"] = (HttpStatusCode.OK, StatusJson) };

		await Client(handler, "admin", "s3cret").GetStatusAsync(CancellationToken.None);

		Assert.That(handler.Requests.Single().Authorization,
			Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:s3cret"))));
	}

	[Test]
	public async Task No_authorization_header_is_sent_without_a_username()
	{
		var handler = new RoutingHandler { ["GET /control/status"] = (HttpStatusCode.OK, StatusJson) };

		await Client(handler).GetStatusAsync(CancellationToken.None);

		Assert.That(handler.Requests.Single().Authorization, Is.Null);
	}

	[TestCase(HttpStatusCode.Unauthorized, AdGuardHomeConnection.Unauthorized)]
	[TestCase(HttpStatusCode.Forbidden, AdGuardHomeConnection.Unauthorized)]
	[TestCase(HttpStatusCode.Found, AdGuardHomeConnection.Redirected)]
	[TestCase(HttpStatusCode.NotFound, AdGuardHomeConnection.Incompatible)]
	[TestCase(HttpStatusCode.UnsupportedMediaType, AdGuardHomeConnection.Incompatible)]
	[TestCase(HttpStatusCode.InternalServerError, AdGuardHomeConnection.Unreachable)]
	public void Http_failures_are_classified(HttpStatusCode status, AdGuardHomeConnection expected)
	{
		var handler = new RoutingHandler { ["GET /control/status"] = (status, "nope") };

		var exception = Assert.ThrowsAsync<AdGuardHomeException>(() => Client(handler).GetStatusAsync(CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(expected));
	}

	[TestCase("<html>login</html>")]
	[TestCase("""{"running":true}""")]
	[TestCase("""{"protection_enabled":"yes","running":true,"version":"x"}""")]
	public void A_response_that_is_not_the_adguard_api_is_incompatible(string body)
	{
		var handler = new RoutingHandler { ["GET /control/status"] = (HttpStatusCode.OK, body) };

		var exception = Assert.ThrowsAsync<AdGuardHomeException>(() => Client(handler).GetStatusAsync(CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(AdGuardHomeConnection.Incompatible));
	}

	[Test]
	public void A_server_that_never_answers_times_out()
	{
		var client = new AdGuardHomeClient(Settings(),
			new HttpClient(new RoutingHandler { NeverAnswers = true }),
			TimeSpan.FromMilliseconds(50));

		var exception = Assert.ThrowsAsync<AdGuardHomeException>(() => client.GetStatusAsync(CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(AdGuardHomeConnection.Timeout));
	}

	[Test]
	public async Task A_paused_server_reports_the_remaining_pause()
	{
		var handler = new RoutingHandler
		{
			["GET /control/status"] = (HttpStatusCode.OK,
				"""{"protection_enabled":false,"protection_disabled_duration":300000,"running":true,"version":"v1"}""")
		};

		var status = await Client(handler).GetStatusAsync(CancellationToken.None);

		Assert.That(status.ProtectionDisabledFor, Is.EqualTo(TimeSpan.FromMinutes(5)));
	}

	[Test]
	public async Task An_older_server_without_a_pause_duration_reports_no_pause()
	{
		var handler = new RoutingHandler
		{
			["GET /control/status"] = (HttpStatusCode.OK, """{"protection_enabled":false,"running":true,"version":"v0.106"}""")
		};

		var status = await Client(handler).GetStatusAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(status.ProtectionEnabled, Is.False);
			Assert.That(status.ProtectionDisabledFor, Is.Null);
		});
	}

	[Test]
	public async Task Pausing_disables_protection_for_the_duration_in_milliseconds()
	{
		var handler = new RoutingHandler { ["POST /control/protection"] = (HttpStatusCode.OK, "OK") };

		await Client(handler).SetProtectionAsync(false, TimeSpan.FromHours(1), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(handler.Requests.Single().Body, Is.EqualTo("""{"enabled":false,"duration":3600000}"""));
			Assert.That(handler.ContentLengths.Single(), Is.Not.Null, "a server without chunked uploads still reads the body");
			Assert.That(handler.ContentTypes.Single(), Is.EqualTo("application/json"),
				"AdGuard Home answers 415 to any other content type");
		});
	}

	[Test]
	public async Task Enabling_sends_no_duration()
	{
		var handler = new RoutingHandler { ["POST /control/protection"] = (HttpStatusCode.OK, "OK") };

		await Client(handler).SetProtectionAsync(true, TimeSpan.FromHours(1), CancellationToken.None);

		Assert.That(handler.Requests.Single().Body, Is.EqualTo("""{"enabled":true}"""));
	}

	[Test]
	public async Task Refreshing_filters_updates_block_and_allow_lists()
	{
		var handler = new RoutingHandler { ["POST /control/filtering/refresh"] = (HttpStatusCode.OK, """{"updated":1}""") };

		await Client(handler).RefreshFiltersAsync(CancellationToken.None);

		Assert.That(handler.Requests.Select(request => request.Body),
			Is.EqualTo(new[] { """{"whitelist":false}""", """{"whitelist":true}""" }));
	}

	[Test]
	public void An_unsupported_command_endpoint_is_incompatible()
	{
		var handler = new RoutingHandler { ["POST /control/protection"] = (HttpStatusCode.MethodNotAllowed, "") };

		var exception = Assert.ThrowsAsync<AdGuardHomeException>(() =>
			Client(handler).SetProtectionAsync(true, null, CancellationToken.None));

		Assert.That(exception!.Failure, Is.EqualTo(AdGuardHomeConnection.Incompatible));
	}

	[Test]
	public async Task A_reverse_proxy_path_is_kept()
	{
		var handler = new RoutingHandler { ["GET /adguard/control/status"] = (HttpStatusCode.OK, StatusJson) };
		var client = new AdGuardHomeClient(
			new AdGuardHomeConnectionSettings(AdGuardHomeEndpoint.TryBuild("https://host/adguard/")!, null, null, false),
			new HttpClient(handler),
			TimeSpan.FromSeconds(5));

		await client.GetStatusAsync(CancellationToken.None);

		Assert.That(handler.Requests.Single().Path, Is.EqualTo("/adguard/control/status"));
	}

	private static AdGuardHomeConnectionSettings Settings(string? username = null, string? password = null)
		=> new(AdGuardHomeEndpoint.TryBuild("http://adguard.local:3000")!, username, password, false);

	private static AdGuardHomeClient Client(HttpMessageHandler handler, string? username = null, string? password = null)
		=> new(Settings(username, password), new HttpClient(handler), TimeSpan.FromSeconds(5));

	private sealed class RoutingHandler : HttpMessageHandler, IEnumerable<KeyValuePair<string, (HttpStatusCode, string)>>
	{
		private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = new(StringComparer.Ordinal);

		public bool NeverAnswers { get; init; }

		public List<(string Path, string? Authorization, string? Body)> Requests { get; } = [];

		public List<long?> ContentLengths { get; } = [];

		public List<string?> ContentTypes { get; } = [];

		public (HttpStatusCode, string) this[string route]
		{
			get => _routes[route];
			set => _routes[route] = value;
		}

		public IEnumerator<KeyValuePair<string, (HttpStatusCode, string)>> GetEnumerator()
			=> _routes.Select(pair => new KeyValuePair<string, (HttpStatusCode, string)>(pair.Key, pair.Value)).GetEnumerator();

		global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			if (NeverAnswers)
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			}

			ContentLengths.Add(request.Content?.Headers.ContentLength);
			ContentTypes.Add(request.Content?.Headers.ContentType?.ToString());
			var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));

			var (status, text) = _routes.TryGetValue($"{request.Method} {request.RequestUri.AbsolutePath}", out var route)
				? route
				: (HttpStatusCode.NotFound, "not found");
			return new HttpResponseMessage(status) { Content = new StringContent(text) };
		}
	}
}
