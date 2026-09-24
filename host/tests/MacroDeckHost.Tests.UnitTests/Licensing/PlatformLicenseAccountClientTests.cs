using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Tests.UnitTests.Companion;
using MacroDeckHost.Tests.UnitTests.Connect;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

[TestFixture]
internal sealed class PlatformLicenseAccountClientTests
{
	private static readonly StorePlatformOptions Options = new() { BaseUrl = new Uri("https://platform.test/") };

	[Test]
	public async Task The_account_license_is_read_as_the_signed_in_user_and_the_long_poll_names_the_revision()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": "a.b.c", "revision": 4 }"""));
		using var client = Client(handler, out _);

		var first = await client.GetAccountLicenseAsync(null, TimeSpan.FromSeconds(50), default);
		var polled = await client.GetAccountLicenseAsync(4, TimeSpan.FromSeconds(50), default);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(new PlatformAccountLicenseResult.Current("a.b.c", 4)));
			Assert.That(polled, Is.EqualTo(new PlatformAccountLicenseResult.Current("a.b.c", 4)));
			Assert.That(handler.Requests.Select(request => request.RequestUri!.PathAndQuery),
				Is.EqualTo(new[]
				{
					"/api/v1/companion-licenses/account", "/api/v1/companion-licenses/account?after=4&wait=50"
				}));
			Assert.That(handler.Requests.Select(request => request.Headers.Authorization?.ToString()),
				Is.All.EqualTo("Bearer access-token"));
		});
	}

	[Test]
	public async Task A_license_is_saved_with_put_and_an_empty_account_reads_as_no_license()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": null, "revision": 0 }"""));
		using var client = Client(handler, out _);

		var result = await client.StoreAccountLicenseAsync("a.b.c", default);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(new PlatformAccountLicenseResult.Current(null, 0)));
			Assert.That(handler.Requests.Single().Method, Is.EqualTo(HttpMethod.Put));
			Assert.That(JsonDocument.Parse(handler.Bodies.Single()).RootElement.GetProperty("license").GetString(),
				Is.EqualTo("a.b.c"));
		});
	}

	[Test]
	public async Task A_signed_out_host_sends_nothing()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": null, "revision": 0 }"""));
		var session = new FakeConnectSessionService();
		using var client =
			new PlatformLicenseAccountClient(handler, Options, session, new LoggerConfiguration().CreateLogger());

		var result = await client.GetAccountLicenseAsync(null, TimeSpan.Zero, default);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<PlatformAccountLicenseResult.SignedOut>());
			Assert.That(handler.Requests, Is.Empty);
		});
	}

	[TestCase(HttpStatusCode.Conflict,
		"""{ "code": "account-license-exists", "license": "x.y.z", "revision": 2 }""",
		"conflict")]
	[TestCase(HttpStatusCode.UnprocessableEntity, """{ "code": "invalid-license" }""", "refused:invalid-license")]
	[TestCase(HttpStatusCode.Forbidden, """{ "code": "license-revoked" }""", "refused:license-revoked")]
	[TestCase(HttpStatusCode.Forbidden, """{ "accountSuspended": true }""", "blocked")]
	[TestCase(HttpStatusCode.Forbidden, """{ "title": "Unknown user" }""", "blocked")]
	[TestCase(HttpStatusCode.Unauthorized, "", "unavailable")]
	[TestCase(HttpStatusCode.NotFound, "", "unavailable")]
	[TestCase(HttpStatusCode.BadRequest, """{ "title": "bad" }""", "unavailable")]
	[TestCase(HttpStatusCode.TooManyRequests, "", "unavailable")]
	[TestCase(HttpStatusCode.ServiceUnavailable, """{ "code": "licensing-not-configured" }""", "unavailable")]
	[TestCase(HttpStatusCode.OK, """{ "license": "a.b.c" }""", "unavailable")]
	[TestCase(HttpStatusCode.OK, "not json", "unavailable")]
	[TestCase(HttpStatusCode.Conflict, """{ "code": "something-else" }""", "unavailable")]
	public async Task Every_answer_maps_to_an_outcome_the_worker_can_act_on(HttpStatusCode status,
		string body,
		string expected)
	{
		using var client = Client(new ScriptedHandler(_ => Json(status, body)), out _);

		var result = await client.StoreAccountLicenseAsync("a.b.c", default);

		Assert.That(Describe(result), Is.EqualTo(expected));
	}

	[Test]
	public async Task An_oversized_answer_is_not_read()
	{
		var padding = new string('a', PlatformLicenseAccountClient.MaximumResponseBytes);
		using var client = Client(new ScriptedHandler(_ =>
				Json(HttpStatusCode.OK, $$"""{ "license": "{{padding}}", "revision": 1 }""")),
			out _);

		var result = await client.GetAccountLicenseAsync(null, TimeSpan.Zero, default);

		Assert.That(result, Is.InstanceOf<PlatformAccountLicenseResult.Unavailable>());
	}

	[Test]
	public async Task Neither_the_access_token_nor_the_license_reaches_the_log()
	{
		using var client = Client(new ScriptedHandler(_ => Json(HttpStatusCode.NotFound, "")), out var sink);

		await client.StoreAccountLicenseAsync("secret.license.token", default);

		var text = string.Join('\n', sink.Events.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture)));
		Assert.Multiple(() =>
		{
			Assert.That(sink.Events, Is.Not.Empty);
			Assert.That(text, Does.Not.Contain("access-token"));
			Assert.That(text, Does.Not.Contain("secret.license.token"));
		});
	}

	private static string Describe(PlatformAccountLicenseResult result)
		=> result switch
		{
			PlatformAccountLicenseResult.Conflict { License: "x.y.z", Revision: 2 } => "conflict",
			PlatformAccountLicenseResult.Refused refused => $"refused:{refused.Code}",
			PlatformAccountLicenseResult.Unavailable { AccountBlocked: true } => "blocked",
			PlatformAccountLicenseResult.Unavailable => "unavailable",
			_ => result.ToString()
		};

	private static PlatformLicenseAccountClient Client(ScriptedHandler handler, out CompanionHarness.CapturingSink sink)
	{
		sink = new CompanionHarness.CapturingSink();
		var session = new FakeConnectSessionService { Current = FakeConnectSessionService.SignedIn(null) };
		return new PlatformLicenseAccountClient(handler,
			Options,
			session,
			new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger());
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string body)
		=> new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
	{
		public List<HttpRequestMessage> Requests { get; } = [];

		public List<string> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Requests.Add(request);
			Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
			return answer(request);
		}
	}
}
