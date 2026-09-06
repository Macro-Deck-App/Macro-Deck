using System.Net;
using MacroDeckHost.Integrations.YtmDesktop;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopConfigFlowTests
{
	private static readonly IConfigFlowContext _context = new FakeConfigFlowContext();

	private static readonly string[] _addressFields = ["host", "port"];

	private FakeYtmDesktopApiClient _client = null!;
	private YtmDesktopConfigFlow _flow = null!;
	private YtmDesktopEndpoint? _requestedEndpoint;

	[TearDown]
	public void TearDown() => _client.Dispose();

	[SetUp]
	public void SetUp()
	{
		_client = new FakeYtmDesktopApiClient();
		_flow = new YtmDesktopConfigFlow(endpoint =>
		{
			_requestedEndpoint = endpoint;
			return _client;
		});
	}

	[Test]
	public async Task The_first_step_asks_for_nothing_and_keeps_the_address_out_of_the_way()
	{
		var result = await _flow.StartAsync(_context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("enable"));
			Assert.That(result.NextStep.Fields, Is.Empty);
			Assert.That(result.NextStep.AdvancedFields.Select(f => f.Name),
				Is.EqualTo(_addressFields));
			Assert.That(result.NextStep.AdvancedFields.Any(f => f.Required), Is.False);
		});
	}

	[Test]
	public async Task An_unreachable_server_is_reported_before_a_code_is_ever_requested()
	{
		_client.MetadataException = new HttpRequestException("refused");

		var result = await SubmitEnable();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Companion Server"));
			Assert.That(_client.AuthCodeCalls, Is.Zero);
		});
	}

	[Test]
	public async Task A_server_without_the_v1_api_names_the_version_it_needs()
	{
		_client.ApiVersions = ["v2"];

		var result = await SubmitEnable();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("2.0.6"));
		});
	}

	[Test]
	public async Task Authorization_being_switched_off_names_the_setting()
	{
		_client.AuthCodeException = Rejected(YtmErrorCodes.AuthorizationDisabled, HttpStatusCode.Forbidden);

		var result = await SubmitEnable();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("enable"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Companion Authorization"));
		});
	}

	[Test]
	public async Task The_code_is_shown_as_a_copy_value_and_never_inside_the_instruction_text()
	{
		_client.AuthCode = "7391";

		var result = await SubmitEnable();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("authorize"));
			Assert.That(result.NextStep.Instructions.SelectMany(i => i.Values).Select(v => v.Value),
				Does.Contain("7391"));
			Assert.That(result.NextStep.Instructions.Any(i =>
					TestLocalization.Resolve(i.Text)?.Contains("7391", StringComparison.Ordinal) == true),
				Is.False);
		});
	}

	[Test]
	public async Task A_granted_authorization_stores_the_token_as_a_secret()
	{
		_client.Token = "abc123";
		await SubmitEnable();

		var result = await SubmitAuthorize();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("YouTube Music Desktop App"));
			Assert.That(result.Values!["token"].IsSecret, Is.True);
			Assert.That(result.Values["token"].Value, Is.EqualTo("abc123"));
			Assert.That(result.Values["host"].IsSecret, Is.False);
			Assert.That(result.Values["host"].Value, Is.EqualTo("127.0.0.1"));
			Assert.That(result.Values["port"].Value, Is.EqualTo("9863"));
		});
	}

	[Test]
	public async Task Localhost_is_stored_as_its_ipv4_literal()
	{
		await SubmitEnable(host: "localhost");

		var result = await SubmitAuthorize(host: "localhost");

		Assert.Multiple(() =>
		{
			Assert.That(result.Values!["host"].Value, Is.EqualTo("127.0.0.1"));
			Assert.That(_requestedEndpoint!.Host, Is.EqualTo("127.0.0.1"));
		});
	}

	[Test]
	public async Task A_host_of_its_own_ends_up_in_the_entry_title()
	{
		await SubmitEnable(host: "192.168.1.20");

		var result = await SubmitAuthorize(host: "192.168.1.20");

		Assert.That(result.EntryTitle, Is.EqualTo("YouTube Music Desktop App (192.168.1.20)"));
	}

	[Test]
	public async Task An_unusable_port_is_reported_on_the_field()
	{
		var result = await _flow.SubmitAsync("enable",
			new Dictionary<string, object?> { ["host"] = "127.0.0.1", ["port"] = 99999 },
			_context,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Does.ContainKey("port"));
		});
	}

	[TestCase(YtmErrorCodes.AuthorizationDenied, HttpStatusCode.Forbidden)]
	[TestCase(YtmErrorCodes.AuthorizationTimeOut, HttpStatusCode.GatewayTimeout)]
	[TestCase(YtmErrorCodes.AuthorizationInvalid, HttpStatusCode.BadRequest)]
	public async Task A_spent_code_is_replaced_with_a_fresh_one(string code, HttpStatusCode status)
	{
		await SubmitEnable();
		_client.TokenException = Rejected(code, status);
		_client.AuthCode = "9999";

		var result = await SubmitAuthorize();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("authorize"));
			Assert.That(result.NextStep.Instructions.SelectMany(i => i.Values).Select(v => v.Value),
				Does.Contain("9999"));
			Assert.That(_client.AuthCodeCalls, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Another_pending_authorization_reuses_the_code_it_already_has()
	{
		_client.AuthCode = "4242";
		await SubmitEnable();
		_client.TokenException = Rejected(YtmErrorCodes.AuthorizationTooMany, HttpStatusCode.ServiceUnavailable);

		var result = await SubmitAuthorize();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.Instructions.SelectMany(i => i.Values).Select(v => v.Value),
				Does.Contain("4242"));
			Assert.That(_client.AuthCodeCalls, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Rate_limiting_does_not_burn_a_code_either()
	{
		_client.AuthCode = "4242";
		await SubmitEnable();
		_client.TokenException = FakeYtmDesktopApiClient.RateLimited();

		var result = await SubmitAuthorize();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(_client.AuthCodeCalls, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Authorization_switched_off_mid_flow_sends_the_user_back_to_the_first_step()
	{
		await SubmitEnable();
		_client.TokenException = Rejected(YtmErrorCodes.AuthorizationDisabled, HttpStatusCode.Forbidden);

		var result = await SubmitAuthorize();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("enable"));
		});
	}

	private Task<ConfigFlowResult> SubmitEnable(string host = "127.0.0.1", int port = 9863)
		=> _flow.SubmitAsync("enable",
			new Dictionary<string, object?> { ["host"] = host, ["port"] = port },
			_context,
			CancellationToken.None);

	private Task<ConfigFlowResult> SubmitAuthorize(string host = "127.0.0.1", int port = 9863)
		=> _flow.SubmitAsync("authorize",
			new Dictionary<string, object?> { ["host"] = host, ["port"] = port },
			_context,
			CancellationToken.None);

	private static YtmDesktopApiException Rejected(string code, HttpStatusCode status)
		=> new(code, status, code);

	private sealed class FakeConfigFlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth { get; } = new FakeOAuthSession();
	}

	private sealed class FakeOAuthSession : IOAuthSession
	{
		public string RedirectUri => "http://localhost/callback";
		public string State => "state";
		public string? AuthorizationCode => null;
	}
}
