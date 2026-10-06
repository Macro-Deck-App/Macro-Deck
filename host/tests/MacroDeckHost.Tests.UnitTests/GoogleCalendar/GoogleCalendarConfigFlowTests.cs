using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.GoogleCalendar;

[TestFixture]
internal sealed class GoogleCalendarConfigFlowTests
{
	private const string CalendarScope = "https://www.googleapis.com/auth/calendar.readonly";

	private GoogleCalendarHarness _harness = null!;
	private FakeConfigFlowContext _context = null!;
	private IConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new GoogleCalendarHarness();
		_context = new FakeConfigFlowContext();
		_flow = _harness.Integration.CreateConfigFlow();
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task The_first_step_asks_for_the_users_own_client_and_shows_the_redirect_uri()
	{
		var step = (await _flow.StartAsync(_context, CancellationToken.None)).NextStep!;

		var instructions = string.Join('\n', step.Instructions.Select(i => TestLocalization.Resolve(i.Text)));
		Assert.Multiple(() =>
		{
			Assert.That(step.Fields.Select(f => f.Name), Is.EqualTo(new[] { "clientId", "clientSecret" }));
			Assert.That(step.Fields[1].Type, Is.EqualTo(ActionParameterType.Secret), "the secret is stored encrypted");
			Assert.That(step.Fields.Select(f => f.DefaultValue), Is.All.Null, "nothing is carried over from other entries");
			Assert.That(step.Instructions.SelectMany(i => i.Values).Select(v => v.Value),
				Does.Contain(_context.RedirectUri));
			Assert.That(instructions, Does.Contain("Desktop app"));
			Assert.That(instructions, Does.Contain("In production").And.Contain("7 days"));
			Assert.That(instructions, Does.Contain("not verified"));
			Assert.That(step.Links.Select(l => l.Url), Has.Some.StartsWith("https://console.cloud.google.com/"));
		});
	}

	[Test]
	public async Task Missing_credentials_are_reported_per_field()
	{
		var result = await SubmitCredentialsAsync(" ", "");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { "clientId", "clientSecret" }));
		});
	}

	[Test]
	public async Task The_sign_in_asks_for_offline_read_only_access_secured_with_a_pkce_challenge()
	{
		var result = await SubmitCredentialsAsync("my-client.apps.googleusercontent.com", "my-secret");
		var authorize = new Uri(result.ExternalUrl!);
		var query = new GoogleRecordedRequest(HttpMethod.Get, authorize, null, string.Empty).Query;

		AnswerTokenRequest(Tokens(refreshToken: "refresh-1"));
		_context.AuthorizationCode = "auth-code";
		await _flow.SubmitAsync(result.ResumeStepId!, new Dictionary<string, object?>(), _context, CancellationToken.None);
		var exchange = _harness.Server.To(GoogleCalendarHarness.TokenPath).Single().Form;

		var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"])))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.External));
			Assert.That(authorize.GetLeftPart(UriPartial.Path), Is.EqualTo("https://accounts.google.com/o/oauth2/v2/auth"));
			Assert.That(query["response_type"], Is.EqualTo("code"));
			Assert.That(query["client_id"], Is.EqualTo("my-client.apps.googleusercontent.com"));
			Assert.That(query["redirect_uri"], Is.EqualTo(_context.RedirectUri));
			Assert.That(query["scope"].Split(' '), Is.EquivalentTo(new[] { CalendarScope, "openid", "email" }));
			Assert.That(query["access_type"], Is.EqualTo("offline"));
			Assert.That(query["prompt"], Is.EqualTo("consent"));
			Assert.That(query["state"], Is.EqualTo(_context.State));
			Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
			Assert.That(exchange["code_verifier"].Length, Is.InRange(43, 128));
			Assert.That(query["code_challenge"], Is.EqualTo(expectedChallenge));
			Assert.That(exchange["grant_type"], Is.EqualTo("authorization_code"));
			Assert.That(exchange["code"], Is.EqualTo("auth-code"));
			Assert.That(exchange["client_secret"], Is.EqualTo("my-secret"));
			Assert.That(exchange["redirect_uri"], Is.EqualTo(_context.RedirectUri));
		});
	}

	[Test]
	public async Task Completing_the_sign_in_keeps_tokens_secret_and_names_the_entry_after_the_account()
	{
		var result = await SignInAsync(Tokens(refreshToken: "refresh-1", expiresIn: 3599));

		var values = result.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("me@example.com"));
			Assert.That(values["refreshToken"], Is.EqualTo(ConfigFlowValue.Secret("refresh-1")));
			Assert.That(values["accessToken"], Is.EqualTo(ConfigFlowValue.Secret("access-1")));
			Assert.That(values["email"], Is.EqualTo(ConfigFlowValue.Plain("me@example.com")));
			Assert.That(values["scope"].IsSecret, Is.False);
			Assert.That(DateTimeOffset.Parse(values["expiresAt"].Value!, CultureInfo.InvariantCulture),
				Is.EqualTo(_harness.Time.Now.AddSeconds(3599)));
		});
	}

	[Test]
	public async Task Without_an_id_token_the_email_comes_from_the_user_info()
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == "/v1/userinfo", HttpStatusCode.OK, """{"email":"other@example.com"}""");

		var result = await SignInAsync(Tokens(refreshToken: "refresh-1", email: null));

		Assert.That(result.EntryTitle, Is.EqualTo("other@example.com"));
	}

	[Test]
	public async Task A_cancelled_sign_in_returns_to_the_first_step()
	{
		var started = await SubmitCredentialsAsync("client", "secret");

		var result = await _flow.SubmitAsync(started.ResumeStepId!,
			new Dictionary<string, object?>(),
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("credentials"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("cancelled"));
		});
	}

	[Test]
	public async Task A_rejected_exchange_asks_to_check_the_client()
	{
		await SubmitCredentialsAsync("client", "wrong-secret");
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath,
			HttpStatusCode.Unauthorized,
			"""{"error":"invalid_client"}""");
		_context.AuthorizationCode = "auth-code";

		var result = await _flow.SubmitAsync("authorize", new Dictionary<string, object?>(), _context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("client secret"));
		});
	}

	[Test]
	public async Task A_grant_without_a_refresh_token_or_calendar_access_is_not_stored()
	{
		var withoutRefresh = await SignInAsync(Tokens(refreshToken: null));
		var withoutCalendar = await SignInAsync(Tokens(refreshToken: "refresh-1", scope: "openid email"));

		Assert.Multiple(() =>
		{
			Assert.That(withoutRefresh.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(withoutRefresh.ErrorMessage), Does.Contain("lasting access"));
			Assert.That(withoutCalendar.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(withoutCalendar.ErrorMessage), Does.Contain("calendars"));
		});
	}

	private async Task<ConfigFlowResult> SignInAsync(string tokenResponse)
	{
		var started = await SubmitCredentialsAsync("client", "secret");
		AnswerTokenRequest(tokenResponse);
		_context.AuthorizationCode = "auth-code";
		return await _flow.SubmitAsync(started.ResumeStepId!,
			new Dictionary<string, object?>(),
			_context,
			CancellationToken.None);
	}

	private Task<ConfigFlowResult> SubmitCredentialsAsync(string clientId, string clientSecret)
		=> _flow.SubmitAsync("credentials",
			new Dictionary<string, object?> { ["clientId"] = clientId, ["clientSecret"] = clientSecret },
			_context,
			CancellationToken.None);

	private void AnswerTokenRequest(string body)
		=> _harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath, HttpStatusCode.OK, body);

	private static string Tokens(
		string? refreshToken,
		int expiresIn = 3600,
		string scope = CalendarScope + " openid https://www.googleapis.com/auth/userinfo.email",
		string? email = "me@example.com")
	{
		var parts = new List<string>
		{
			"\"access_token\":\"access-1\"",
			$"\"expires_in\":{expiresIn}",
			$"\"scope\":\"{scope}\"",
			"\"token_type\":\"Bearer\""
		};
		if (refreshToken is not null)
		{
			parts.Add($"\"refresh_token\":\"{refreshToken}\"");
		}

		if (email is not null)
		{
			parts.Add($"\"id_token\":\"{GoogleCalendarHarness.IdToken(email)}\"");
		}

		return "{" + string.Join(',', parts) + "}";
	}
}
