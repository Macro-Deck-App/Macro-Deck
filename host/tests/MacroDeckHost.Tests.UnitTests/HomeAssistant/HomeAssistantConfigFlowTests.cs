using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantConfigFlowTests
{
	private FakeHomeAssistantClient _client = null!;
	private HomeAssistantConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_client = NewClient();
		_flow = new HomeAssistantConfigFlow(() => _client);
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	public async Task The_flow_starts_on_the_connection_step()
	{
		var result = await _flow.StartAsync(null!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("connection"));
			Assert.That(result.NextStep?.Fields.Select(field => field.Name),
				Is.EquivalentTo(new List<string> { "baseUrl", "token" }));
		});
	}

	[Test]
	public async Task A_blank_url_is_a_field_error()
	{
		var result = await SubmitConnection(baseUrl: "   ");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key("baseUrl"));
			Assert.That(_client.ConnectCount, Is.Zero);
		});
	}

	[Test]
	public async Task An_unparseable_url_is_a_field_error_naming_the_expected_shape()
	{
		var result = await SubmitConnection(baseUrl: "ftp://nowhere");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.FieldErrors?["baseUrl"]),
				Does.Contain(HomeAssistantEndpoint.ExampleUrl));
		});
	}

	[Test]
	public async Task A_blank_token_is_a_field_error()
	{
		var result = await SubmitConnection(token: " ");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key("token"));
			Assert.That(_client.ConnectCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_tls_failure_explains_where_to_trust_the_certificate()
	{
		_client.ConnectException = new HomeAssistantTlsException("untrusted", new InvalidOperationException());

		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("certificate"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Does.Contain("operating system certificate store"));
		});
	}

	[Test]
	public async Task An_unreachable_server_names_the_default_port()
	{
		_client.ConnectException = new InvalidOperationException("connection refused");

		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Could not reach"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("8123"));
		});
	}

	[Test]
	public async Task A_timeout_after_the_handshake_does_not_blame_the_address()
	{
		_client.TimingOutRequests.Add("get_states");

		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("did not answer in time"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Not.Contain("Check the address"));
		});
	}

	[Test]
	public async Task A_socket_that_never_speaks_the_protocol_is_reported_distinctly()
	{
		_client.ConnectException = new HomeAssistantRequestException("no auth_required");

		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Does.Contain("not the Home Assistant WebSocket API"));
		});
	}

	[Test]
	public async Task A_rejected_token_is_a_field_error()
	{
		_client.ConnectException = new HomeAssistantAuthenticationException("Home Assistant rejected the token");

		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key("token"));
		});
	}

	[Test]
	public async Task A_successful_test_completes_the_flow_using_the_location_name()
	{
		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Home Assistant (Home)"));
		});
	}

	[Test]
	public async Task Completion_carries_neither_the_token_the_url_nor_any_watched_entity_key()
	{
		var result = await SubmitConnection();

		Assert.Multiple(() =>
		{
			Assert.That(result.Values, Is.Not.Null);
			Assert.That(result.Values, Is.Empty);
		});
	}

	[Test]
	public async Task A_location_less_server_falls_back_to_the_host_in_the_title()
	{
		_client.Responses["get_config"] = """{ "version": "2026.8.0" }""";

		var result = await SubmitConnection();

		Assert.That(result.EntryTitle, Is.EqualTo("Home Assistant (homeassistant.local)"));
	}

	[Test]
	public async Task An_unknown_step_is_rejected()
	{
		var result = await _flow.SubmitAsync("nonsense",
			new Dictionary<string, object?>(StringComparer.Ordinal),
			null!,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private static FakeHomeAssistantClient NewClient()
		=> new()
		{
			Responses =
			{
				["get_states"] = "[]",
				["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }"""
			}
		};

	private Task<ConfigFlowResult> SubmitConnection(
		string baseUrl = "http://homeassistant.local:8123",
		string token = "the-token")
		=> _flow.SubmitAsync("connection",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["baseUrl"] = baseUrl, ["token"] = token },
			null!,
			CancellationToken.None);
}
