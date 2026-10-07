using System.Net;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.MicrosoftCalendar;
using MacroDeckHost.Tests.UnitTests.GoogleCalendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.MicrosoftCalendar;

[TestFixture]
internal sealed class MicrosoftCalendarConfigFlowTests
{
	private const string Email = "megan@contoso.com";
	private const string ClientId = MicrosoftCalendarHarness.ClientId;

	private MicrosoftCalendarHarness _harness = null!;
	private FakeConfigFlowContext _context = null!;
	private IConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new MicrosoftCalendarHarness();
		_harness.Calendars(MicrosoftCalendarHarness.Calendar("cal-1", "Calendar", ownerAddress: Email, isDefault: true),
			MicrosoftCalendarHarness.Calendar("cal-2", "Holidays", ownerAddress: Email),
			MicrosoftCalendarHarness.Calendar("cal-3", "Calendar", ownerAddress: "alex@contoso.com",
				ownerName: "Alex Wilber"),
			MicrosoftCalendarHarness.Calendar("cal-4", "Family", ownerAddress: "outlook_A1B2@outlook.com"));
		_context = new FakeConfigFlowContext();
		_flow = _harness.Integration.CreateConfigFlow();
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task The_first_step_only_asks_to_sign_in_and_keeps_an_own_app_optional()
	{
		var step = (await _flow.StartAsync(_context, CancellationToken.None)).NextStep!;

		var instructions = string.Join('\n', step.Instructions.Select(i => TestLocalization.Resolve(i.Text)));
		var ownApp = TestLocalization.Resolve(step.AdvancedFields.Single(f => f.Name == "clientId").Description);
		Assert.Multiple(() =>
		{
			Assert.That(step.Fields, Is.Empty, "nothing has to be entered to sign in");
			Assert.That(step.AdvancedFields.Select(f => (f.Name, f.Required)),
				Is.EqualTo(new[] { ("clientId", false), ("tenant", false) }));
			Assert.That(step.AdvancedFields.Select(f => f.Type), Has.None.EqualTo(ActionParameterType.Secret),
				"a public client has no secret to ask for");
			Assert.That(instructions, Does.Not.Contain(_context.RedirectUri).And.Not.Contain("Calendars.Read"),
				"the own-app setup stays out of sight until Advanced is opened");
			Assert.That(step.Links, Is.Empty);
			Assert.That(ownApp, Does.Contain(_context.RedirectUri).And.Contain("Mobile and desktop applications")
				.And.Contain("Calendars.Read").And.Contain("needs a directory"));
		});
	}

	[Test]
	public async Task Without_an_own_app_the_sign_in_uses_macro_decks_app_and_keeps_it_for_the_account()
	{
		var external = await _flow.SubmitAsync("app", new Dictionary<string, object?>(), _context, CancellationToken.None);
		var query = GraphRecordedRequest.ParseQuery(new Uri(external.ExternalUrl!).Query.TrimStart('?'));
		var calendars = await SignInAsync(external);
		var exchange = _harness.Server.To(MicrosoftCalendarHarness.TokenPath).Single().Form;
		var result = await _flow.SubmitAsync(calendars.NextStep!.StepId,
			new Dictionary<string, object?> { ["calendarIds"] = """["cal-1"]""" },
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(query["client_id"], Is.EqualTo(MicrosoftOAuth.DefaultClientId));
			Assert.That(exchange["client_id"], Is.EqualTo(MicrosoftOAuth.DefaultClientId));
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!["signInClientId"], Is.EqualTo(ConfigFlowValue.Plain(MicrosoftOAuth.DefaultClientId)),
				"refresh tokens belong to the app that issued them");
			Assert.That(result.Values!.Keys, Has.None.EqualTo("clientId").And.None.EqualTo("tenant"),
				"editing the connection shows Advanced as the user left it");
		});
	}

	[Test]
	public async Task An_application_id_that_is_not_one_and_a_malformed_tenant_are_reported_per_field()
	{
		var result = await SubmitAppAsync("my app", "not a tenant/../x");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { "clientId", "tenant" }));
		});
	}

	[TestCase(null, "https://login.microsoftonline.com/common/oauth2/v2.0/authorize")]
	[TestCase("Contoso.onmicrosoft.com", "https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/authorize")]
	[TestCase("consumers", "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize")]
	public async Task The_sign_in_asks_the_tenant_for_offline_read_only_calendar_access_with_pkce(
		string? tenant,
		string authority)
	{
		var result = await SubmitAppAsync(ClientId, tenant);
		var authorize = new Uri(result.ExternalUrl!);
		var query = GraphRecordedRequest.ParseQuery(authorize.Query.TrimStart('?'));

		await SignInAsync(result, tokenPath: authority.Replace("https://login.microsoftonline.com", "")
			.Replace("authorize", "token"));
		var exchange = _harness.Server.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
			.Form;

		var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"])))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.External));
			Assert.That(authorize.GetLeftPart(UriPartial.Path), Is.EqualTo(authority));
			Assert.That(query["client_id"], Is.EqualTo(ClientId));
			Assert.That(query["redirect_uri"], Is.EqualTo(_context.RedirectUri));
			Assert.That(query["state"], Is.EqualTo(_context.State));
			Assert.That(query["scope"].Split(' '), Is.EquivalentTo(new[]
			{
				"openid", "profile", "email", "offline_access", "https://graph.microsoft.com/Calendars.Read"
			}));
			Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
			Assert.That(query["code_challenge"], Is.EqualTo(challenge));
			Assert.That(exchange["grant_type"], Is.EqualTo("authorization_code"));
			Assert.That(exchange["code"], Is.EqualTo("auth-code"));
			Assert.That(exchange["redirect_uri"], Is.EqualTo(_context.RedirectUri));
			Assert.That(exchange, Does.Not.ContainKey("client_secret"));
		});
	}

	[Test]
	public async Task After_sign_in_every_calendar_including_shared_ones_is_offered_and_all_are_chosen_at_first()
	{
		var step = (await SignInAsync(await SubmitAppAsync(ClientId))).NextStep!;
		var field = step.Fields.Single();

		Assert.Multiple(() =>
		{
			Assert.That(step.StepId, Is.EqualTo("calendars"));
			Assert.That(field.Name, Is.EqualTo("calendarIds"));
			Assert.That(field.Type, Is.EqualTo(ActionParameterType.MultiSelect));
			Assert.That(field.Options!.Select(o => (o.Value, TestLocalization.Resolve(o.Label))),
				Is.EqualTo(new[]
				{
					("cal-1", "Calendar"), ("cal-2", "Holidays"), ("cal-3", "Calendar (shared by Alex Wilber)"),
					("cal-4", "Family")
				}));
			Assert.That(field.DefaultValue, Is.EqualTo(new[] { "cal-1", "cal-2", "cal-3", "cal-4" }));
		});
	}

	[Test]
	public async Task Completing_keeps_the_tokens_secret_and_names_the_entry_after_the_account()
	{
		var calendars = await SignInAsync(await SubmitAppAsync(ClientId));
		var result = await _flow.SubmitAsync(calendars.NextStep!.StepId,
			new Dictionary<string, object?> { ["calendarIds"] = """["cal-1","cal-3"]""" },
			_context,
			CancellationToken.None);

		var values = result.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo(Email));
			Assert.That(values["accessToken"], Is.EqualTo(ConfigFlowValue.Secret("access-1")));
			Assert.That(values["refreshToken"], Is.EqualTo(ConfigFlowValue.Secret("refresh-1")));
			Assert.That(values.Where(v => !v.Value.IsSecret).Select(v => v.Value.Value),
				Has.None.Contains("access-1").And.None.Contains("refresh-1"));
			Assert.That(values["email"], Is.EqualTo(ConfigFlowValue.Plain(Email)));
			Assert.That(values["accountKey"], Is.EqualTo(ConfigFlowValue.Plain("tid-1/oid-1")));
		});
	}

	[Test]
	public async Task Reconnecting_an_account_offers_its_previous_choice_of_calendars()
	{
		_harness.AddAccount(Email, ["cal-2", "gone"], accountKey: "tid-1/oid-1");
		await _harness.StartAsync();
		_flow = _harness.Integration.CreateConfigFlow();

		var step = (await SignInAsync(await SubmitAppAsync(ClientId))).NextStep!;

		Assert.That(step.Fields.Single().DefaultValue, Is.EqualTo(new[] { "cal-2" }));
	}

	[Test]
	public async Task Choosing_no_calendar_is_refused()
	{
		var calendars = await SignInAsync(await SubmitAppAsync(ClientId));
		var result = await _flow.SubmitAsync("calendars",
			new Dictionary<string, object?> { ["calendarIds"] = "[]" },
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(calendars.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { "calendarIds" }));
		});
	}

	[Test]
	public async Task Calendars_that_cannot_be_listed_after_sign_in_end_on_an_error_and_store_nothing()
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.CalendarsPath,
			HttpStatusCode.InternalServerError,
			"""{"error":{"code":"generalException","message":"An unspecified error has occurred."}}""");

		var result = await SignInAsync(await SubmitAppAsync(ClientId));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("app"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("calendars could not be read"));
			Assert.That(result.Values, Is.Null);
		});
	}

	[Test]
	public async Task A_cancelled_sign_in_returns_to_the_first_step()
	{
		var external = await SubmitAppAsync(ClientId);
		var result = await _flow.SubmitAsync(external.ResumeStepId!, new Dictionary<string, object?>(), _context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("app"));
			Assert.That(_harness.Server.Requests, Is.Empty);
		});
	}

	[TestCase(7000218, "Mobile and desktop applications")]
	[TestCase(65001, "administrator")]
	[TestCase(70002, "Microsoft did not accept the sign-in")]
	public async Task A_refused_exchange_says_what_to_fix(int code, string hint)
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath,
			HttpStatusCode.BadRequest,
			$$"""{"error":"invalid_client","error_description":"AADSTS{{code}}: refused.","error_codes":[{{code}}]}""");

		var result = await SignInAsync(await SubmitAppAsync(ClientId), answerToken: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain(hint));
		});
	}

	[TestCase("""{"token_type":"Bearer","scope":"Calendars.Read openid profile email","expires_in":3599,"access_token":"access-1"}""")]
	[TestCase("""{"token_type":"Bearer","scope":"openid profile email","expires_in":3599,"access_token":"access-1","refresh_token":"refresh-1"}""")]
	public async Task A_grant_without_lasting_or_calendar_access_is_not_stored(string tokenResponse)
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath, HttpStatusCode.OK, tokenResponse);

		var result = await SignInAsync(await SubmitAppAsync(ClientId), answerToken: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(_harness.Server.To(MicrosoftCalendarHarness.CalendarsPath), Is.Empty);
		});
	}

	private Task<ConfigFlowResult> SubmitAppAsync(string clientId, string? tenant = null)
		=> _flow.SubmitAsync("app",
			new Dictionary<string, object?> { ["clientId"] = clientId, ["tenant"] = tenant },
			_context,
			CancellationToken.None);

	private async Task<ConfigFlowResult> SignInAsync(
		ConfigFlowResult external,
		bool answerToken = true,
		string tokenPath = MicrosoftCalendarHarness.TokenPath)
	{
		if (answerToken)
		{
			_harness.Server.On(r => r.Uri.AbsolutePath == tokenPath,
				HttpStatusCode.OK,
				$$"""{"token_type":"Bearer","scope":"https://graph.microsoft.com/Calendars.Read openid profile email","expires_in":3599,"ext_expires_in":3599,"access_token":"access-1","refresh_token":"refresh-1","id_token":"{{MicrosoftCalendarHarness.IdToken(Email, "oid-1", "tid-1")}}"}""");
		}

		_context.AuthorizationCode = "auth-code";
		return await _flow.SubmitAsync(external.ResumeStepId!, new Dictionary<string, object?>(), _context,
			CancellationToken.None);
	}
}
