using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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

	[Test]
	public async Task A_promo_code_is_posted_as_typed_with_the_account_token()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": "a.b.c", "revision": 1 }"""));
		using var client = Client(handler, out _);

		var result = await client.RedeemPromoCodeAsync("abcd-efgh-jkmn-pqrs", default);

		var request = handler.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(new PlatformPromoCodeResult.Redeemed("a.b.c")));
			Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
			Assert.That(request.RequestUri!.PathAndQuery, Is.EqualTo("/api/v1/companion-licenses/promo-code"));
			Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer access-token"));
			Assert.That(JsonDocument.Parse(handler.Bodies.Single()).RootElement.GetProperty("code").GetString(),
				Is.EqualTo("abcd-efgh-jkmn-pqrs"));
		});
	}

	[Test]
	public async Task A_signed_out_host_does_not_send_a_promo_code()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, "{}"));
		using var client = new PlatformLicenseAccountClient(handler,
			Options,
			new FakeConnectSessionService(),
			new LoggerConfiguration().CreateLogger());

		var result = await client.RedeemPromoCodeAsync("ABCD", default);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<PlatformPromoCodeResult.SignedOut>());
			Assert.That(handler.Requests, Is.Empty);
		});
	}

	[TestCase(HttpStatusCode.NotFound, """{ "code": "invalid-promo-code" }""", "rejected:Invalid")]
	[TestCase(HttpStatusCode.BadRequest, """{ "title": "bad" }""", "rejected:Invalid")]
	[TestCase(HttpStatusCode.Gone, """{ "code": "promo-code-expired" }""", "rejected:Expired")]
	[TestCase(HttpStatusCode.Conflict, """{ "code": "promo-code-redeemed" }""", "rejected:AlreadyRedeemed")]
	[TestCase(HttpStatusCode.Conflict,
		"""{ "code": "account-license-exists", "license": "x.y.z", "revision": 2 }""",
		"exists:x.y.z")]
	[TestCase(HttpStatusCode.Forbidden, """{ "code": "license-revoked" }""", "rejected:Revoked")]
	[TestCase(HttpStatusCode.Forbidden, """{ "title": "suspended" }""", "suspended")]
	[TestCase(HttpStatusCode.Unauthorized, "", "signedOut")]
	[TestCase(HttpStatusCode.TooManyRequests, "", "limited:")]
	[TestCase(HttpStatusCode.ServiceUnavailable, """{ "code": "promo-codes-not-configured" }""", "unavailable")]
	[TestCase(HttpStatusCode.ServiceUnavailable, """{ "code": "licensing-not-configured" }""", "unavailable")]
	[TestCase(HttpStatusCode.NotFound, "", "unavailable")]
	[TestCase(HttpStatusCode.OK, """{ "revision": 1 }""", "unavailable")]
	[TestCase(HttpStatusCode.OK, "not json", "unavailable")]
	[TestCase(HttpStatusCode.Conflict, """{ "code": "account-license-exists" }""", "unavailable")]
	public async Task Every_promo_code_answer_maps_to_an_outcome_the_ui_can_explain(HttpStatusCode status,
		string body,
		string expected)
	{
		using var client = Client(new ScriptedHandler(_ => Json(status, body)), out _);

		var result = await client.RedeemPromoCodeAsync("ABCD", default);

		Assert.That(DescribePromo(result), Is.EqualTo(expected));
	}

	[Test]
	public async Task A_rate_limited_promo_code_carries_the_retry_after_delay()
	{
		var response = Json(HttpStatusCode.TooManyRequests, "");
		response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
		using var client = Client(new ScriptedHandler(_ => response), out _);

		var result = await client.RedeemPromoCodeAsync("ABCD", default);

		Assert.That(result, Is.EqualTo(new PlatformPromoCodeResult.RateLimited(TimeSpan.FromSeconds(120))));
	}

	[Test]
	public async Task A_promo_code_never_reaches_the_log()
	{
		using var client = Client(new ScriptedHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "")), out var sink);

		await client.RedeemPromoCodeAsync("SECRET-PROMO-CODE", default);

		var text = string.Join('\n', sink.Events.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture)));
		Assert.Multiple(() =>
		{
			Assert.That(sink.Events, Is.Not.Empty);
			Assert.That(text, Does.Not.Contain("SECRET-PROMO-CODE"));
			Assert.That(text, Does.Not.Contain("access-token"));
		});
	}

	private static string DescribePromo(PlatformPromoCodeResult result)
		=> result switch
		{
			PlatformPromoCodeResult.Redeemed redeemed => $"redeemed:{redeemed.License}",
			PlatformPromoCodeResult.AccountLicenseExists existing => $"exists:{existing.License}",
			PlatformPromoCodeResult.Rejected rejected => $"rejected:{rejected.Reason}",
			PlatformPromoCodeResult.AccountSuspended => "suspended",
			PlatformPromoCodeResult.SignedOut => "signedOut",
			PlatformPromoCodeResult.RateLimited limited => $"limited:{limited.RetryAfter}",
			PlatformPromoCodeResult.Unavailable => "unavailable",
			_ => result.ToString()!
		};

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
