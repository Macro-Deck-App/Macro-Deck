using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Configuration;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Batteries;
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
	public void TearDown()
	{
		_client.Dispose();
	}

	[Test]
	public async Task The_flow_starts_on_the_connection_step()
	{
		var result = await _flow.StartAsync(NoOAuthConfigFlowContext.Instance, CancellationToken.None);

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
		var result = await SubmitConnection("   ");

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
		var result = await SubmitConnection("ftp://nowhere");

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

	[Test]
	public async Task Battery_sensors_are_offered_in_a_second_step_with_the_previous_selection_preselected()
	{
		_client.Responses["get_states"] = States(("sensor.phone_battery", "Phone"),
			("sensor.watch_battery", "Watch"),
			("sensor.temperature", null));

		var result = await SubmitConnection(previousBatteries: """["sensor.watch_battery","sensor.gone"]""");

		var field = result.NextStep?.Fields.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("batteries"));
			Assert.That(field?.Name, Is.EqualTo(HomeAssistantConfigKeys.BatteryEntities));
			Assert.That(field?.Required, Is.False);
			Assert.That(field?.Options?.Select(option => option.Value),
				Is.EquivalentTo(new[] { "sensor.phone_battery", "sensor.watch_battery" }));
			Assert.That(field?.DefaultValue, Is.EqualTo(new[] { "sensor.watch_battery" }));
		});
	}

	[Test]
	public async Task Without_battery_sensors_the_flow_completes_and_keeps_the_stored_selection()
	{
		var result = await SubmitConnection(previousBatteries: """["sensor.phone_battery"]""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values, Does.Not.ContainKey(HomeAssistantConfigKeys.BatteryEntities));
		});
	}

	[Test]
	public async Task Picked_battery_sensors_are_stored_and_unknown_ids_are_dropped()
	{
		_client.Responses["get_states"] = States(("sensor.phone_battery", "Phone"), ("sensor.watch_battery", "Watch"));
		await SubmitConnection();

		var result = await SubmitBatteries("""["sensor.phone_battery","sensor.injected"]""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Home Assistant (Home)"));
			Assert.That(result.Values?[HomeAssistantConfigKeys.BatteryEntities].Value,
				Is.EqualTo("""["sensor.phone_battery"]"""));
		});
	}

	[Test]
	public async Task An_empty_selection_is_stored_as_an_empty_list()
	{
		_client.Responses["get_states"] = States(("sensor.phone_battery", "Phone"));
		await SubmitConnection(previousBatteries: """["sensor.phone_battery"]""");

		var result = await SubmitBatteries("[]");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values?[HomeAssistantConfigKeys.BatteryEntities].Value, Is.EqualTo("[]"));
		});
	}

	[Test]
	public async Task More_battery_sensors_than_a_provider_may_publish_is_a_field_error()
	{
		var ids = Enumerable.Range(0, BatteryLimits.MaxDevicesPerProvider + 1)
			.Select(index => $"sensor.battery_{index}")
			.ToArray();
		_client.Responses["get_states"] = States([.. ids.Select(id => (id, (string?)id))]);
		await SubmitConnection();

		var result = await SubmitBatteries(JsonSerializer.Serialize(ids));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("batteries"));
			Assert.That(TestLocalization.Resolve(result.FieldErrors?[HomeAssistantConfigKeys.BatteryEntities]),
				Does.Contain(BatteryLimits.MaxDevicesPerProvider.ToString(CultureInfo.InvariantCulture)));
		});
	}

	[Test]
	public async Task The_battery_step_cannot_be_submitted_before_a_connection_was_tested()
	{
		var result = await SubmitBatteries("[]");

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private static string States(params (string EntityId, string? FriendlyName)[] batteries)
	{
		var entries = batteries.Select(battery => new Dictionary<string, object?>
		{
			["entity_id"] = battery.EntityId,
			["state"] = "50",
			["attributes"] = battery.FriendlyName is null
				? new Dictionary<string, object?> { ["device_class"] = "temperature" }
				: new Dictionary<string, object?>
				{
					["device_class"] = "battery",
					["friendly_name"] = battery.FriendlyName
				}
		});
		return JsonSerializer.Serialize(entries);
	}

	private static FakeHomeAssistantClient NewClient()
	{
		return new FakeHomeAssistantClient
		{
			Responses =
			{
				["get_states"] = "[]",
				["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }"""
			}
		};
	}

	private Task<ConfigFlowResult> SubmitConnection(
		string baseUrl = "http://homeassistant.local:8123",
		string token = "the-token",
		string? previousBatteries = null)
	{
		var input = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["baseUrl"] = baseUrl,
			["token"] = token
		};
		if (previousBatteries is not null)
		{
			input[HomeAssistantConfigKeys.BatteryEntities] = previousBatteries;
		}

		return _flow.SubmitAsync("connection", input, null!, CancellationToken.None);
	}

	private Task<ConfigFlowResult> SubmitBatteries(string selection)
	{
		return _flow.SubmitAsync("batteries",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["baseUrl"] = "http://homeassistant.local:8123",
				["token"] = "the-token",
				[HomeAssistantConfigKeys.BatteryEntities] = selection
			},
			null!,
			CancellationToken.None);
	}
}
