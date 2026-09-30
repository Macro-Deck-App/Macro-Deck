using System.Diagnostics;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Tests.UnitTests.HomeAssistant;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Integrations.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantControlsTests
{
	private const string DeskLamp =
		"""{ "entity_id": "light.desk", "state": "on", "attributes": { "brightness": 128, "supported_color_modes": ["color_temp"], "min_color_temp_kelvin": 2000, "max_color_temp_kelvin": 6500, "color_temp_kelvin": 3000 } }""";

	private const string HallwayLampOff =
		"""{ "entity_id": "light.hallway", "state": "off", "attributes": { "brightness": null, "supported_color_modes": ["brightness"] } }""";

	private const string ThreeSpeedFan =
		"""{ "entity_id": "fan.ceiling", "state": "on", "attributes": { "percentage": 33, "percentage_step": 33.333333333333336 } }""";

	private const string TargetHelper =
		"""{ "entity_id": "input_number.target", "state": "42.0", "attributes": { "min": 10.0, "max": 90.0, "step": 0.5 } }""";

	private static readonly Uri _uri = new("ws://homeassistant.local:8123/api/websocket");

	[Test]
	public async Task A_light_offers_its_brightness_as_a_writable_percentage()
	{
		var provider = CatalogOf(DeskLamp);

		var children = await provider.DiscoverAsync(new VariableCatalogQuery { ParentId = "entity/light.desk" });
		var brightness = children.Items.Single(item => item.Id == "entity/light.desk/brightness_pct");
		var reading = await provider.ReadAsync("entity/light.desk/brightness_pct");

		Assert.Multiple(() =>
		{
			Assert.That(brightness.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(brightness.CanWrite, Is.True);
			Assert.That(brightness.Unit, Is.EqualTo("%"));
			Assert.That(brightness.SemanticKind, Is.EqualTo(VariableSemanticKinds.Percentage));
			Assert.That(brightness.Name, Is.EqualTo("ha_light_desk_brightness_pct"));
			Assert.That(reading.Value, Is.EqualTo(50));
			Assert.That((reading.Min, reading.Max, reading.Step), Is.EqualTo((0d, 100d, 1d)));
		});
	}

	[Test]
	public async Task A_light_that_is_off_still_offers_its_brightness_and_reads_zero()
	{
		var provider = CatalogOf(HallwayLampOff);

		var children = await provider.DiscoverAsync(new VariableCatalogQuery { ParentId = "entity/light.hallway" });
		var resolved = await provider.ResolveAsync("entity/light.hallway/brightness_pct");
		var reading = await provider.ReadAsync("entity/light.hallway/brightness_pct");

		Assert.Multiple(() =>
		{
			Assert.That(children.Items.Select(item => item.Id), Does.Contain("entity/light.hallway/brightness_pct"));
			Assert.That(resolved?.CanWrite, Is.True);
			Assert.That(reading.Value, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_light_that_cannot_dim_is_not_offered_a_brightness()
	{
		var provider = CatalogOf(
			"""{ "entity_id": "light.plug", "state": "on", "attributes": { "supported_color_modes": ["onoff"] } }""");

		var children = await provider.DiscoverAsync(new VariableCatalogQuery { ParentId = "entity/light.plug" });

		Assert.That(children.Items.Select(item => item.DisplayName.Literal), Is.EquivalentTo(["state", "supported_color_modes", "attributes"]));
	}

	[Test]
	public async Task The_raw_brightness_attribute_stays_read_only_and_keeps_its_value()
	{
		var provider = CatalogOf(DeskLamp, HallwayLampOff);

		var definition = await provider.ResolveAsync("entity/light.desk/brightness");
		var on = await provider.ReadAsync("entity/light.desk/brightness");
		var off = await provider.ReadAsync("entity/light.hallway/brightness");

		Assert.Multiple(() =>
		{
			Assert.That(definition?.CanWrite, Is.False);
			Assert.That(on.Value, Is.EqualTo(128));
			Assert.That(on.Min, Is.Null);
			Assert.That(off.Value, Is.Null);
		});
	}

	[Test]
	public async Task A_fan_reports_its_speed_with_the_range_and_step_of_its_speeds()
	{
		var provider = CatalogOf(ThreeSpeedFan);

		var definition = await provider.ResolveAsync("entity/fan.ceiling/percentage");
		var reading = await provider.ReadAsync("entity/fan.ceiling/percentage");

		Assert.Multiple(() =>
		{
			Assert.That(definition?.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(definition?.CanWrite, Is.True);
			Assert.That(reading.Value, Is.EqualTo(33));
			Assert.That((reading.Min, reading.Max), Is.EqualTo((0d, 100d)));
			Assert.That(reading.Step, Is.EqualTo(33.333333333333336).Within(1e-9));
		});
	}

	[Test]
	public async Task A_number_helper_keeps_its_raw_state_and_gains_its_range()
	{
		var provider = CatalogOf(TargetHelper);

		var definition = await provider.ResolveAsync("entity/input_number.target/state");
		var reading = await provider.ReadAsync("entity/input_number.target/state");

		Assert.Multiple(() =>
		{
			Assert.That(definition?.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(definition?.CanWrite, Is.True);
			Assert.That(definition?.Name, Is.EqualTo("ha_input_number_target"));
			Assert.That(reading.Value, Is.EqualTo("42.0"));
			Assert.That((reading.Min, reading.Max, reading.Step), Is.EqualTo((10d, 90d, 0.5d)));
		});
	}

	[Test]
	public async Task A_control_sends_its_value_on_release()
	{
		var provider = CatalogOf(DeskLamp);

		var definition = await provider.ResolveAsync("entity/light.desk/brightness_pct");

		Assert.That(definition?.Write?.CommitOnRelease, Is.True);
	}

	[Test]
	public async Task A_control_is_writable_from_its_id_before_home_assistant_has_answered()
	{
		var provider = new HomeAssistantVariableCatalog(() => HomeAssistantCatalog.Empty);

		var control = await provider.ResolveAsync("entity/light.desk/brightness_pct");
		var number = await provider.ResolveAsync("entity/input_number.target/state");
		var sensor = await provider.ResolveAsync("entity/sensor.power/state");
		var typed = await provider.ResolveAsync("light.desk/brightness_pct");

		Assert.Multiple(() =>
		{
			Assert.That(control?.CanWrite, Is.True);
			Assert.That(control?.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(control?.Unit, Is.EqualTo("%"));
			Assert.That(number?.CanWrite, Is.True);
			Assert.That(sensor, Is.Null);
			Assert.That(typed, Is.Null);
		});
	}

	[Test]
	public async Task A_control_stays_writable_while_home_assistant_has_not_reported_its_entity_yet()
	{
		var provider = CatalogOf(DeskLamp);

		var control = await provider.ResolveAsync("entity/light.still_loading/brightness_pct");
		var attribute = await provider.ResolveAsync("entity/light.still_loading/brightness");
		var typed = await provider.ResolveAsync("light.still_loading/brightness_pct");

		Assert.Multiple(() =>
		{
			Assert.That(control?.CanWrite, Is.True);
			Assert.That(attribute, Is.Null);
			Assert.That(typed, Is.Null);
		});
	}

	[Test]
	public async Task A_fan_that_is_off_reads_zero_and_can_be_started_from_its_speed()
	{
		await using var home = await ConnectedAsync(
			"""{ "entity_id": "fan.ceiling", "state": "off", "attributes": { "percentage": null, "percentage_step": 1 } }""");

		var reading = await home.Provider.ReadAsync("entity/fan.ceiling/percentage");
		var result = await home.Provider.SetValueAsync("entity/fan.ceiling/percentage", 40d);

		var call = home.LastCall();
		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.EqualTo(0));
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(call?["service"], Is.EqualTo("set_percentage"));
			Assert.That(Data(call)?["percentage"], Is.EqualTo(40));
		});
	}

	[Test]
	public async Task A_state_change_publishes_a_subscribed_control_once_with_its_range()
	{
		var provider = CatalogOf(DeskLamp);
		var sink = new RecordingSink();
		await provider.OnAttachedAsync(sink);
		await provider.SubscribeAsync(["entity/light.desk/brightness_pct"]);

		provider.Push(State(
			"""{ "entity_id": "light.desk", "state": "on", "attributes": { "brightness": 204, "supported_color_modes": ["color_temp"] } }"""));

		var published = sink.Values.Single();
		Assert.Multiple(() =>
		{
			Assert.That(published.Id, Is.EqualTo("entity/light.desk/brightness_pct"));
			Assert.That(published.Reading.Value, Is.EqualTo(80));
			Assert.That((published.Reading.Min, published.Reading.Max), Is.EqualTo((0d, 100d)));
		});
	}

	[TestCase(DeskLamp, "light.desk", "brightness_pct", 40d, "light", "turn_on", "brightness_pct", 40d)]
	[TestCase(DeskLamp, "light.desk", "color_temp_kelvin", 4000d, "light", "turn_on", "color_temp_kelvin", 4000d)]
	[TestCase(ThreeSpeedFan, "fan.ceiling", "percentage", 66.666666666667, "fan", "set_percentage", "percentage", 66d)]
	[TestCase(ThreeSpeedFan, "fan.ceiling", "percentage", 100d, "fan", "set_percentage", "percentage", 100d)]
	[TestCase(TargetHelper, "input_number.target", "state", 55.5, "input_number", "set_value", "value", 55.5)]
	[TestCase("""{ "entity_id": "number.gain", "state": "3", "attributes": { "min": 0, "max": 10, "step": 1 } }""",
		"number.gain", "state", 7d, "number", "set_value", "value", 7d)]
	[TestCase("""{ "entity_id": "cover.blind", "state": "open", "attributes": { "current_position": 70, "current_tilt_position": 50 } }""",
		"cover.blind", "current_position", 30d, "cover", "set_cover_position", "position", 30d)]
	[TestCase("""{ "entity_id": "cover.blind", "state": "open", "attributes": { "current_position": 70, "current_tilt_position": 50 } }""",
		"cover.blind", "current_tilt_position", 20d, "cover", "set_cover_tilt_position", "tilt_position", 20d)]
	[TestCase("""{ "entity_id": "valve.garden", "state": "open", "attributes": { "current_position": 100 } }""",
		"valve.garden", "current_position", 25d, "valve", "set_valve_position", "position", 25d)]
	[TestCase("""{ "entity_id": "media_player.tv", "state": "playing", "attributes": { "volume_level": 0.35 } }""",
		"media_player.tv", "volume_level", 0.5, "media_player", "volume_set", "volume_level", 0.5)]
	[TestCase("""{ "entity_id": "climate.living", "state": "heat", "attributes": { "temperature": 21.0, "min_temp": 7, "max_temp": 35, "target_temp_step": 0.5, "humidity": 40 } }""",
		"climate.living", "temperature", 22.5, "climate", "set_temperature", "temperature", 22.5)]
	[TestCase("""{ "entity_id": "climate.living", "state": "heat", "attributes": { "temperature": 21.0, "min_temp": 7, "max_temp": 35, "target_temp_step": 0.5, "humidity": 40 } }""",
		"climate.living", "humidity", 45d, "climate", "set_humidity", "humidity", 45d)]
	[TestCase("""{ "entity_id": "water_heater.boiler", "state": "eco", "attributes": { "temperature": 50, "min_temp": 30, "max_temp": 70 } }""",
		"water_heater.boiler", "temperature", 55d, "water_heater", "set_temperature", "temperature", 55d)]
	[TestCase("""{ "entity_id": "humidifier.bedroom", "state": "on", "attributes": { "humidity": 40, "min_humidity": 30, "max_humidity": 80 } }""",
		"humidifier.bedroom", "humidity", 60d, "humidifier", "set_humidity", "humidity", 60d)]
	public async Task Moving_a_control_calls_the_service_home_assistant_adjusts_it_with(
		string entity,
		string entityId,
		string leaf,
		double value,
		string domain,
		string service,
		string field,
		double sent)
	{
		await using var home = await ConnectedAsync(entity);

		var result = await home.Provider.SetValueAsync($"entity/{entityId}/{leaf}", value);

		var call = home.LastCall();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(call?["domain"], Is.EqualTo(domain));
			Assert.That(call?["service"], Is.EqualTo(service));
			Assert.That(Data(call)?[field], Is.EqualTo(sent));
			Assert.That(Data(call), Has.Count.EqualTo(1));
			Assert.That(Target(call)?["entity_id"], Is.EqualTo(entityId));
		});
	}

	[Test]
	public async Task A_value_written_as_text_is_read_as_a_number()
	{
		await using var home = await ConnectedAsync(TargetHelper);

		var result = await home.Provider.SetValueAsync("entity/input_number.target/state", "61.5");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(Data(home.LastCall())?["value"], Is.EqualTo(61.5));
		});
	}

	[Test]
	public async Task Dimming_a_light_to_zero_turns_it_off()
	{
		await using var home = await ConnectedAsync(DeskLamp);

		var result = await home.Provider.SetValueAsync("entity/light.desk/brightness_pct", 0d);

		var call = home.LastCall();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(call?["service"], Is.EqualTo("turn_off"));
			Assert.That(Target(call)?["entity_id"], Is.EqualTo("light.desk"));
		});
	}

	[Test]
	public async Task A_light_that_is_off_is_turned_on_at_the_chosen_brightness()
	{
		await using var home = await ConnectedAsync(HallwayLampOff);

		var result = await home.Provider.SetValueAsync("entity/light.hallway/brightness_pct", 35d);

		var call = home.LastCall();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(call?["service"], Is.EqualTo("turn_on"));
			Assert.That(Data(call)?["brightness_pct"], Is.EqualTo(35));
		});
	}

	[Test]
	public async Task A_value_beyond_the_entitys_range_is_sent_as_its_nearest_limit()
	{
		await using var home = await ConnectedAsync(TargetHelper);

		await home.Provider.SetValueAsync("entity/input_number.target/state", 500d);

		Assert.That(Data(home.LastCall())?["value"], Is.EqualTo(90));
	}

	[TestCase("""{ "entity_id": "media_player.tv", "state": "off", "attributes": { } }""", "media_player.tv/volume_level")]
	[TestCase("""{ "entity_id": "input_number.target", "state": "unavailable", "attributes": { "min": 10.0, "max": 90.0 } }""",
		"input_number.target/state")]
	[TestCase("""{ "entity_id": "light.desk", "state": "unavailable", "attributes": { "supported_color_modes": ["brightness"] } }""",
		"light.desk/brightness_pct")]
	public async Task A_control_with_no_value_right_now_refuses_and_sends_nothing(string entity, string resource)
	{
		await using var home = await ConnectedAsync(entity);

		var result = await home.Provider.SetValueAsync("entity/" + resource, 30d);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
			Assert.That(home.LastCall(), Is.Null);
		});
	}

	[TestCase("entity/sensor.power/state")]
	[TestCase("entity/light.desk/brightness")]
	[TestCase("entity/light.desk/attributes")]
	[TestCase("homeassistant-entity-count")]
	public async Task A_value_that_is_not_a_control_is_not_writable_and_sends_nothing(string resource)
	{
		await using var home = await ConnectedAsync(DeskLamp,
			"""{ "entity_id": "sensor.power", "state": "123.4", "attributes": { "unit_of_measurement": "W" } }""");

		var result = await home.Provider.SetValueAsync(resource, 30d);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
			Assert.That(home.LastCall(), Is.Null);
		});
	}

	[Test]
	public async Task Text_that_is_not_a_number_is_an_invalid_value()
	{
		await using var home = await ConnectedAsync(DeskLamp);

		var result = await home.Provider.SetValueAsync("entity/light.desk/brightness_pct", "bright");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.InvalidValue));
			Assert.That(home.LastCall(), Is.Null);
		});
	}

	[Test]
	public async Task A_write_without_a_connection_is_unavailable()
	{
		var provider = CatalogOf(DeskLamp);

		var result = await provider.SetValueAsync("entity/light.desk/brightness_pct", 30d);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
	}

	[Test]
	public async Task A_call_home_assistant_rejects_fails_the_write()
	{
		await using var home = await ConnectedAsync(DeskLamp);
		home.Client.FailingRequests.Add("call_service");

		var result = await home.Provider.SetValueAsync("entity/light.desk/brightness_pct", 30d);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Failed));
	}

	private static IReadOnlyDictionary<string, object?>? Data(IReadOnlyDictionary<string, object?>? call)
		=> call?["service_data"] as IReadOnlyDictionary<string, object?>;

	private static IReadOnlyDictionary<string, object?>? Target(IReadOnlyDictionary<string, object?>? call)
		=> call?["target"] as IReadOnlyDictionary<string, object?>;

	private static HomeAssistantVariableCatalog CatalogOf(params string[] entities)
	{
		var states = entities.Select(State).ToDictionary(state => state.EntityId, StringComparer.Ordinal);
		var catalog = new HomeAssistantCatalog { Entities = states };
		return new HomeAssistantVariableCatalog(() => catalog);
	}

	private static HomeAssistantEntityState State(string json)
	{
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		return new HomeAssistantEntityState(root.GetProperty("entity_id").GetString()!,
			root.GetProperty("state").GetString()!,
			root.GetProperty("attributes").Clone());
	}

	private static async Task<ConnectedHome> ConnectedAsync(params string[] entities)
	{
		var client = new FakeHomeAssistantClient
		{
			Responses =
			{
				["get_states"] = "[" + string.Join(",", entities) + "]",
				["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }""",
				["get_services"] = "{}",
				["config/area_registry/list"] = "[]",
				["config/device_registry/list"] = "[]",
				["config/entity_registry/list"] = "[]"
			}
		};

		var connection = new HomeAssistantConnection(() => client, _uri, "the-token");
		connection.Start();

		var stopwatch = Stopwatch.StartNew();
		while (!connection.IsConnected && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(10);
		}

		Assert.That(connection.IsConnected, Is.True, "Timed out waiting for the connection to come up.");

		var provider = new HomeAssistantVariableCatalog(() => connection.Catalog, connection: () => connection);
		return new ConnectedHome(connection, client, provider);
	}

	private sealed record ConnectedHome(
		HomeAssistantConnection Connection,
		FakeHomeAssistantClient Client,
		HomeAssistantVariableCatalog Provider) : IAsyncDisposable
	{
		public IReadOnlyDictionary<string, object?>? LastCall() => Client.PayloadOf("call_service");

		public ValueTask DisposeAsync()
		{
			Connection.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingSink : IVariableSink
	{
		public List<VariableValue> Values { get; } = [];

		public Task PublishAsync(IReadOnlyCollection<VariableValue> values, CancellationToken cancellationToken = default)
		{
			Values.AddRange(values);
			return Task.CompletedTask;
		}

		public Task InvalidateCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
