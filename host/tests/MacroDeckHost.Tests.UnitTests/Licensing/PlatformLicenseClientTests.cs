using System.Net;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Infrastructure.Licensing;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Licensing;

[TestFixture]
internal sealed class PlatformLicenseClientTests
{
	private static readonly StorePlatformOptions Options = new() { BaseUrl = new Uri("https://platform.test/") };

	[Test]
	public async Task A_proof_is_posted_to_the_platform_and_a_license_is_returned()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": "header.payload.signature" }"""));
		using var client = Client(handler);

		var result = await client.IssueCompanionLicenseAsync(GooglePlay(), default);

		var body = JsonDocument.Parse(handler.Bodies.Single()).RootElement;
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(new PlatformLicenseIssueResult.Issued("header.payload.signature")));
			Assert.That(handler.Requests.Single().Method, Is.EqualTo(HttpMethod.Post));
			Assert.That(handler.Requests.Single().RequestUri,
				Is.EqualTo(new Uri("https://platform.test/api/v1/companion-licenses")));
			Assert.That(body.GetProperty("platform").GetString(), Is.EqualTo("google-play"));
			Assert.That(body.GetProperty("productId").GetString(), Is.EqualTo(CompanionLicenseTokens.Product));
			Assert.That(body.GetProperty("purchaseToken").GetString(), Is.EqualTo("purchase-token"));
			Assert.That(body.GetProperty("packageName").GetString(), Is.EqualTo("app.macrodeck.companion"));
			Assert.That(body.TryGetProperty("legacyKind", out _), Is.False);
		});
	}

	[Test]
	public async Task A_macro_deck_2_app_transaction_is_posted_with_its_legacy_kind()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": "header.payload.signature" }"""));
		using var client = Client(handler);

		var result = await client.IssueCompanionLicenseAsync(new CompanionLicenseProof
			{
				Platform = "app-store-legacy", LegacyKind = "appTransaction", SignedPayload = "app.transaction.jws",
				ProductId = CompanionLicenseTokens.Product
			},
			default);

		var body = JsonDocument.Parse(handler.Bodies.Single()).RootElement;
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(new PlatformLicenseIssueResult.Issued("header.payload.signature")));
			Assert.That(body.GetProperty("platform").GetString(), Is.EqualTo("app-store-legacy"));
			Assert.That(body.GetProperty("legacyKind").GetString(), Is.EqualTo("appTransaction"));
			Assert.That(body.GetProperty("signedPayload").GetString(), Is.EqualTo("app.transaction.jws"));
			Assert.That(body.GetProperty("productId").GetString(), Is.EqualTo(CompanionLicenseTokens.Product));
			Assert.That(body.GetProperty("transactionId").ValueKind, Is.EqualTo(JsonValueKind.Null));
		});
	}

	[TestCase("app-store-legacy", null, CompanionLicenseTokens.Product)]
	[TestCase("app-store-legacy", "receipt", CompanionLicenseTokens.Product)]
	[TestCase("app-store-legacy", "appTransaction", "something_else")]
	[TestCase("test", null, CompanionLicenseTokens.Product)]
	[TestCase("google-play", null, "something_else")]
	public async Task A_proof_the_platform_does_not_issue_for_is_refused_without_a_request(string platform,
		string? legacyKind,
		string productId)
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "license": "x.y.z" }"""));
		using var client = Client(handler);

		var result = await client.IssueCompanionLicenseAsync(
			new CompanionLicenseProof
			{
				Platform = platform, LegacyKind = legacyKind, ProductId = productId, PurchaseToken = "t",
				SignedPayload = "a.b.c"
			},
			default);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<PlatformLicenseIssueResult.Refused>());
			Assert.That(handler.Requests, Is.Empty);
		});
	}

	[TestCase(HttpStatusCode.BadRequest, """{ "errors": {} }""", "invalid-proof")]
	[TestCase(HttpStatusCode.RequestEntityTooLarge, "", "proof-too-large")]
	[TestCase(HttpStatusCode.Forbidden, """{ "code": "license-revoked" }""", "license-revoked")]
	[TestCase(HttpStatusCode.UnprocessableEntity, """{ "code": "purchase-refunded" }""", "purchase-refunded")]
	[TestCase(HttpStatusCode.UnprocessableEntity, """{ "code": "sandbox-purchase" }""", "sandbox-purchase")]
	public async Task A_definitive_answer_is_a_refusal(HttpStatusCode status, string body, string code)
	{
		using var client = Client(new ScriptedHandler(_ => Json(status, body)));

		var result = await client.IssueCompanionLicenseAsync(GooglePlay(), default);

		Assert.That(result, Is.EqualTo(new PlatformLicenseIssueResult.Refused(code)));
	}

	[TestCase(HttpStatusCode.ServiceUnavailable, """{ "code": "store-unavailable" }""", false, "store-unavailable")]
	[TestCase(HttpStatusCode.TooManyRequests, "", false, null)]
	[TestCase(HttpStatusCode.InternalServerError, "", false, null)]
	[TestCase(HttpStatusCode.BadGateway, "<html>", false, null)]
	[TestCase(HttpStatusCode.Forbidden, "<html>blocked</html>", false, null)]
	[TestCase(HttpStatusCode.UnprocessableEntity, """{ "code": "purchase-not-found" }""", true, "purchase-not-found")]
	[TestCase(HttpStatusCode.UnprocessableEntity, """{ "code": "purchase-not-completed" }""", true,
		"purchase-not-completed")]
	[TestCase(HttpStatusCode.OK, """{ "license": "" }""", false, null)]
	public async Task A_transient_answer_is_retried_with_the_platform_code(HttpStatusCode status,
		string body,
		bool purchasePending,
		string? code)
	{
		using var client = Client(new ScriptedHandler(_ => Json(status, body)));

		var result = await client.IssueCompanionLicenseAsync(GooglePlay(), default);

		Assert.That(result, Is.EqualTo(new PlatformLicenseIssueResult.Retry(null, purchasePending, code)));
	}

	[Test]
	public async Task Retry_after_is_passed_on()
	{
		using var client = Client(new ScriptedHandler(_ =>
		{
			var response = Json(HttpStatusCode.ServiceUnavailable, """{ "code": "store-busy" }""");
			response.Headers.TryAddWithoutValidation("Retry-After", "120");
			return response;
		}));

		var result = await client.IssueCompanionLicenseAsync(GooglePlay(), default);

		Assert.That(result,
			Is.EqualTo(new PlatformLicenseIssueResult.Retry(TimeSpan.FromSeconds(120), false, "store-busy")));
	}

	[Test]
	public async Task No_connection_is_retried()
	{
		using var client = Client(new ScriptedHandler(_ => throw new HttpRequestException("offline")));

		var issued = await client.IssueCompanionLicenseAsync(GooglePlay(), default);
		var revocations = await client.GetRevokedLicenseIdsAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(issued, Is.EqualTo(new PlatformLicenseIssueResult.Retry(null, false)));
			Assert.That(revocations, Is.Null);
		});
	}

	[Test]
	public async Task The_revocation_list_keeps_only_well_formed_license_ids()
	{
		var valid = new string('a', 32);
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK,
			JsonSerializer.Serialize(new { revokedLicenseIds = new[] { valid, "NOT-AN-ID", new string('B', 32), valid } })));
		using var client = Client(handler);

		var ids = await client.GetRevokedLicenseIdsAsync(default);

		Assert.Multiple(() =>
		{
			Assert.That(ids, Is.EqualTo(new[] { valid }));
			Assert.That(handler.Requests.Single().RequestUri,
				Is.EqualTo(new Uri("https://platform.test/api/v1/companion-licenses/revocations")));
		});
	}

	[Test]
	public async Task A_revocation_list_larger_than_the_host_accepts_keeps_the_previous_copy()
	{
		var ids = Enumerable.Range(0, PlatformLicenseClient.MaximumRevokedLicenseIds + 1)
			.Select(index => Guid.NewGuid().ToString("N"));
		using var client = Client(new ScriptedHandler(_ =>
			Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { revokedLicenseIds = ids }))));

		Assert.That(await client.GetRevokedLicenseIdsAsync(default), Is.Null);
	}

	[TestCase(HttpStatusCode.ServiceUnavailable, "")]
	[TestCase(HttpStatusCode.OK, "not json")]
	[TestCase(HttpStatusCode.OK, "{}")]
	public async Task An_unusable_revocation_answer_keeps_the_previous_copy(HttpStatusCode status, string body)
	{
		using var client = Client(new ScriptedHandler(_ => Json(status, body)));

		Assert.That(await client.GetRevokedLicenseIdsAsync(default), Is.Null);
	}

	private static PlatformLicenseClient Client(ScriptedHandler handler)
		=> new(handler, Options, new LoggerConfiguration().CreateLogger());

	private static CompanionLicenseProof GooglePlay()
		=> new()
		{
			Platform = "google-play", ProductId = CompanionLicenseTokens.Product, PurchaseToken = "purchase-token",
			PackageName = "app.macrodeck.companion", OrderId = "GPA.1", LegacyKind = "ignored"
		};

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
