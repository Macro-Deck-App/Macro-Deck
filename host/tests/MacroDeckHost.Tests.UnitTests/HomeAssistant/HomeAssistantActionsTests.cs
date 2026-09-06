using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Actions;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantActionsTests
{
	private static readonly Uri _uri = new("ws://homeassistant.local:8123/api/websocket");

	private HomeAssistantConnection? _connection;
	private FakeHomeAssistantClient? _client;

	[TearDown]
	public void TearDown()
	{
		_connection?.Dispose();
		_client?.Dispose();
	}


	[Test]
	public void ReadText_trims_and_treats_blank_as_missing()
	{
		var parameters = Parameters(("a", "  Kitchen Lamp  "), ("b", "   "));

		Assert.Multiple(() =>
		{
			Assert.That(HomeAssistantActionValues.ReadText(parameters, "a"), Is.EqualTo("Kitchen Lamp"));
			Assert.That(HomeAssistantActionValues.ReadText(parameters, "b"), Is.Null);
			Assert.That(HomeAssistantActionValues.ReadText(parameters, "missing"), Is.Null);
		});
	}

	[TestCase("de-DE")]
	[TestCase("en-US")]
	public void A_wire_text_parameter_parses_and_serialises_identically_across_cultures(string cultureName)
	{
		var original = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

			var parameters = Parameters(("brightnessPercent", "41.6"));
			var value = HomeAssistantActionValues.ReadNumber(parameters, "brightnessPercent", 0, 100);

			Assert.That(value, Is.EqualTo(41.6d).Within(0.0001));

			var json = JsonSerializer.Serialize(value, HomeAssistantJson.Options);
			Assert.That(json, Is.EqualTo("41.6"));
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	[Test]
	public void ReadNumber_clamps_into_the_given_range()
	{
		var parameters = Parameters(("v", "500"));

		Assert.That(HomeAssistantActionValues.ReadNumber(parameters, "v", 0, 100), Is.EqualTo(100d));
	}

	[Test]
	public void ReadNumber_answers_null_for_an_unparseable_value()
	{
		var parameters = Parameters(("v", "not a number"));

		Assert.That(HomeAssistantActionValues.ReadNumber(parameters, "v"), Is.Null);
	}

	[Test]
	public void ReadList_accepts_a_json_array_and_a_comma_separated_string()
	{
		var jsonParameters = Parameters(("entities", """["light.a","light.b"]"""));
		var commaParameters = Parameters(("entities", "light.a, light.b"));

		Assert.Multiple(() =>
		{
			Assert.That(HomeAssistantActionValues.ReadList(jsonParameters, "entities"),
				Is.EqualTo(new List<string> { "light.a", "light.b" }));
			Assert.That(HomeAssistantActionValues.ReadList(commaParameters, "entities"),
				Is.EqualTo(new List<string> { "light.a", "light.b" }));
		});
	}

	[Test]
	public void ReadData_accepts_a_json_object_string()
	{
		var parameters = Parameters(("data", """{ "brightness": 200, "flag": true }"""));

		var data = HomeAssistantActionValues.ReadData(parameters, "data");

		Assert.Multiple(() =>
		{
			Assert.That(data?["brightness"], Is.EqualTo(200L));
			Assert.That(data?["flag"], Is.EqualTo(true));
		});
	}

	[Test]
	public void ReadData_accepts_a_string_map_from_the_key_value_editor()
	{
		var parameters = Parameters(("data", new Dictionary<string, string> { ["mode"] = "eco" }));

		var data = HomeAssistantActionValues.ReadData(parameters, "data");

		Assert.That(data?["mode"], Is.EqualTo("eco"));
	}

	[Test]
	public void ReadData_answers_null_for_empty_or_broken_json()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HomeAssistantActionValues.ReadData(Parameters(("data", "")), "data"), Is.Null);
			Assert.That(HomeAssistantActionValues.ReadData(Parameters(("data", "not json")), "data"), Is.Null);
			Assert.That(HomeAssistantActionValues.ReadData(Parameters(("data", "{}")), "data"), Is.Null);
		});
	}

	[Test]
	public void ReadRgb_reads_a_hash_prefixed_hex_triplet()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HomeAssistantActionValues.ReadRgb("#ff8800"), Is.EqualTo(new List<int> { 255, 136, 0 }));
			Assert.That(HomeAssistantActionValues.ReadRgb("not a color"), Is.Null);
			Assert.That(HomeAssistantActionValues.ReadRgb(null), Is.Null);
		});
	}


	[Test]
	public async Task Every_executor_fails_with_NotConnected_when_there_is_no_live_connection()
	{
		var definitions = HomeAssistantActions.Create(() => null, new HomeAssistantVariableAccessor());

		Assert.Multiple(async () =>
		{
			foreach (var definition in definitions)
			{
				var result = await definition.CreateExecutor()
					.ExecuteAsync(new ActionExecutionContext
					{
						Parameters = new Dictionary<string, object>()
					});

				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed), definition.Id);
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected), definition.Id);
			}
		});

		await Task.CompletedTask;
	}

	[Test]
	public async Task An_entity_the_live_cache_does_not_know_fails_loudly_with_NotFound()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }]""");

		var definition = new LightActionDefinition(() => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "light.renamed", ["state"] = "on"
				}
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("light.renamed"));
		});
	}


	[Test]
	public async Task CallService_merges_entities_areas_and_devices_into_one_target()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }]""");

		var definition = new CallServiceActionDefinition(() => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["domain"] = "light",
					["service"] = "turn_on",
					["entities"] = new List<string> { "light.kitchen" },
					["areas"] = new List<string> { "kitchen" },
					["devices"] = new List<string> { "device-1" }
				}
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));

		var payload = _client!.PayloadOf("call_service");
		var target = payload?["target"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(target?["entity_id"], Is.EqualTo(new List<string> { "light.kitchen" }));
			Assert.That(target?["area_id"], Is.EqualTo(new List<string> { "kitchen" }));
			Assert.That(target?["device_id"], Is.EqualTo(new List<string> { "device-1" }));
		});
	}

	[Test]
	public async Task CallService_accepts_data_as_a_json_string()
	{
		await ConnectAsync("[]");
		var definition = new CallServiceActionDefinition(() => _connection);

		var jsonResult = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["domain"] = "notify", ["service"] = "persistent_notification", ["data"] = """{ "message": "hi" }"""
				}
			});

		Assert.That(jsonResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(
			(_client!.PayloadOf("call_service")?["service_data"] as IReadOnlyDictionary<string, object?>)?["message"],
			Is.EqualTo("hi"));
	}

	[Test]
	public async Task An_unresolvable_entity_in_the_call_service_target_is_rejected()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }]""");

		var definition = new CallServiceActionDefinition(() => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["domain"] = "light", ["service"] = "turn_on", ["entities"] = new List<string> { "light.missing" }
				}
			});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task CallService_options_filter_by_domain()
	{
		await ConnectAsync(
			"""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }, { "entity_id": "switch.fan", "state": "off", "attributes": {} }]""",
			getServicesJson: """{ "light": { "turn_on": {} }, "switch": { "turn_on": {} } }""");

		var definition = new CallServiceActionDefinition(() => _connection);
		var options = await definition.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "entities",
				CurrentParameters = new Dictionary<string, object?> { ["domain"] = "light" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options.Select(o => o.Value), Is.EqualTo(new List<string> { "light.kitchen" }));
			Assert.That(options.AllowsCustomValue, Is.True);
		});
	}


	[Test]
	public async Task EntityPower_calls_the_homeassistant_forwarding_domain()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "off", "attributes": {} }]""");

		var definition = new EntityPowerActionDefinition("turn-on", "Turn On", "", "turn_on", () => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["entities"] = new List<string> { "light.kitchen" } }
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.Multiple(() =>
		{
			Assert.That(_client!.PayloadOf("call_service")?["domain"], Is.EqualTo("homeassistant"));
			Assert.That(_client!.PayloadOf("call_service")?["service"], Is.EqualTo("turn_on"));
		});
	}

	[Test]
	public async Task EntityPower_rejects_an_empty_selection()
	{
		await ConnectAsync("[]");

		var definition = new EntityPowerActionDefinition("turn-on", "Turn On", "", "turn_on", () => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}


	[Test]
	public async Task LightSet_turning_on_sends_brightness_color_and_temperature()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "off", "attributes": {} }]""");

		var definition = new LightActionDefinition(() => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "light.kitchen",
					["state"] = "on",
					["brightnessPercent"] = "50",
					["color"] = "#ff8800",
					["colorTempKelvin"] = "3000",
					["transition"] = "2"
				}
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		var data = _client!.PayloadOf("call_service")?["service_data"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(_client!.PayloadOf("call_service")?["service"], Is.EqualTo("turn_on"));
			Assert.That(data?["brightness_pct"], Is.EqualTo(50d));
			Assert.That(data?["rgb_color"], Is.EqualTo(new List<int> { 255, 136, 0 }));
			Assert.That(data?["color_temp_kelvin"], Is.EqualTo(3000));
			Assert.That(data?["transition"], Is.EqualTo(2d));
		});
	}

	[Test]
	public async Task LightSet_turning_off_sends_no_color_or_brightness_fields()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }]""");

		var definition = new LightActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "light.kitchen", ["state"] = "off", ["brightnessPercent"] = "50"
				}
			});

		var payload = _client!.PayloadOf("call_service");
		Assert.Multiple(() =>
		{
			Assert.That(payload?["service"], Is.EqualTo("turn_off"));
			Assert.That(payload?["service_data"], Is.Null);
		});
	}


	[TestCase("open", "open_cover")]
	[TestCase("close", "close_cover")]
	[TestCase("stop", "stop_cover")]
	public async Task CoverSet_maps_the_command_to_the_matching_service(string command, string expectedService)
	{
		await ConnectAsync("""[{ "entity_id": "cover.blinds", "state": "closed", "attributes": {} }]""");

		var definition = new CoverActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["entity"] = "cover.blinds", ["command"] = command }
			});

		Assert.That(_client!.PayloadOf("call_service")?["service"], Is.EqualTo(expectedService));
	}

	[Test]
	public async Task CoverSet_position_sends_the_position_field()
	{
		await ConnectAsync("""[{ "entity_id": "cover.blinds", "state": "closed", "attributes": {} }]""");

		var definition = new CoverActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "cover.blinds", ["command"] = "set_position", ["position"] = "40"
				}
			});

		var data = _client!.PayloadOf("call_service")?["service_data"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(_client!.PayloadOf("call_service")?["service"], Is.EqualTo("set_cover_position"));
			Assert.That(data?["position"], Is.EqualTo(40d));
		});
	}


	[Test]
	public async Task MediaPlayer_volume_set_converts_percent_to_a_zero_to_one_level()
	{
		await ConnectAsync("""[{ "entity_id": "media_player.tv", "state": "playing", "attributes": {} }]""");

		var definition = new MediaPlayerActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "media_player.tv", ["command"] = "volume_set", ["volumePercent"] = "25"
				}
			});

		var data = _client!.PayloadOf("call_service")?["service_data"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(_client!.PayloadOf("call_service")?["service"], Is.EqualTo("volume_set"));
			Assert.That(data?["volume_level"], Is.EqualTo(0.25d));
		});
	}

	[Test]
	public async Task MediaPlayer_mute_sends_is_volume_muted_true()
	{
		await ConnectAsync("""[{ "entity_id": "media_player.tv", "state": "playing", "attributes": {} }]""");

		var definition = new MediaPlayerActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["entity"] = "media_player.tv", ["command"] = "mute" }
			});

		var data = _client!.PayloadOf("call_service")?["service_data"] as IReadOnlyDictionary<string, object?>;
		Assert.That(data?["is_volume_muted"], Is.EqualTo(true));
	}


	[Test]
	public async Task ClimateSet_makes_one_call_per_filled_field()
	{
		await ConnectAsync("""[{ "entity_id": "climate.living", "state": "heat", "attributes": {} }]""");

		var definition = new ClimateActionDefinition(() => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "climate.living", ["temperature"] = "21.5", ["hvacMode"] = "heat"
				}
			});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.Multiple(() =>
		{
			Assert.That(_client!.RequestNames.Count(name => name == "call_service"), Is.EqualTo(2));
			Assert.That(_client!.Requests.Any(r
				=> r.Command == "call_service" && r.Payload?["service"] as string == "set_temperature"));
			Assert.That(_client!.Requests.Any(r
				=> r.Command == "call_service" && r.Payload?["service"] as string == "set_hvac_mode"));
		});
	}

	[Test]
	public async Task ClimateSet_with_nothing_filled_in_fails_rather_than_doing_nothing_silently()
	{
		await ConnectAsync("""[{ "entity_id": "climate.living", "state": "heat", "attributes": {} }]""");

		var definition = new ClimateActionDefinition(() => _connection);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["entity"] = "climate.living" }
			});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task ClimateSet_mode_pickers_depend_on_the_selected_entitys_attributes()
	{
		await ConnectAsync("""
						   [{ "entity_id": "climate.living", "state": "heat",
						      "attributes": { "hvac_modes": ["heat", "cool", "off"] } }]
						   """);

		var definition = new ClimateActionDefinition(() => _connection);
		var options = await definition.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "hvacMode",
				CurrentParameters = new Dictionary<string, object?> { ["entity"] = "climate.living" }
			},
			CancellationToken.None);

		Assert.That(options.Options.Select(o => o.Value), Is.EqualTo(new List<string> { "heat", "cool", "off" }));
	}


	[Test]
	public async Task ActivateScene_calls_scene_turn_on()
	{
		await ConnectAsync("""[{ "entity_id": "scene.movie", "state": "scening", "attributes": {} }]""");

		var definition = new SceneActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["entity"] = "scene.movie" }
			});

		var payload = _client!.PayloadOf("call_service");
		Assert.Multiple(() =>
		{
			Assert.That(payload?["domain"], Is.EqualTo("scene"));
			Assert.That(payload?["service"], Is.EqualTo("turn_on"));
		});
	}


	[Test]
	public async Task RunScript_passes_data_as_variables()
	{
		await ConnectAsync("""[{ "entity_id": "script.good_morning", "state": "off", "attributes": {} }]""");

		var definition = new ScriptActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "script.good_morning", ["data"] = """{ "target": "kitchen" }"""
				}
			});

		var data = _client!.PayloadOf("call_service")?["service_data"] as IReadOnlyDictionary<string, object?>;
		var variables = data?["variables"] as IReadOnlyDictionary<string, object?>;
		Assert.That(variables?["target"], Is.EqualTo("kitchen"));
	}


	[Test]
	public async Task AutomationControl_trigger_defaults_skip_condition_to_true()
	{
		await ConnectAsync("""[{ "entity_id": "automation.morning", "state": "on", "attributes": {} }]""");

		var definition = new AutomationActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "automation.morning", ["command"] = "trigger"
				}
			});

		var payload = _client!.PayloadOf("call_service");
		var data = payload?["service_data"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(payload?["service"], Is.EqualTo("trigger"));
			Assert.That(data?["skip_condition"], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task AutomationControl_enable_disable_and_toggle_send_no_data()
	{
		await ConnectAsync("""[{ "entity_id": "automation.morning", "state": "on", "attributes": {} }]""");

		var definition = new AutomationActionDefinition(() => _connection);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "automation.morning", ["command"] = "turn_off"
				}
			});

		var payload = _client!.PayloadOf("call_service");
		Assert.Multiple(() =>
		{
			Assert.That(payload?["service"], Is.EqualTo("turn_off"));
			Assert.That(payload?["service_data"], Is.Null);
		});
	}


	[Test]
	public async Task GetEntityState_with_no_attribute_reads_the_state_itself()
	{
		await ConnectAsync(
			"""[{ "entity_id": "sensor.temp", "state": "21.5", "attributes": { "unit_of_measurement": "C" } }]""");

		var accessor = new HomeAssistantVariableAccessor { Current = new RecordingVariableApi() };
		var definition = new GetEntityStateActionDefinition(() => _connection, accessor);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["entity"] = "sensor.temp", ["variable"] = "temp" }
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(((RecordingVariableApi)accessor.Current!).Written["temp"], Is.EqualTo("21.5"));
		});
	}

	[Test]
	public async Task GetEntityState_reads_a_named_attribute()
	{
		await ConnectAsync(
			"""[{ "entity_id": "sensor.temp", "state": "21.5", "attributes": { "unit_of_measurement": "C" } }]""");

		var accessor = new HomeAssistantVariableAccessor { Current = new RecordingVariableApi() };
		var definition = new GetEntityStateActionDefinition(() => _connection, accessor);
		await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "sensor.temp", ["attribute"] = "unit_of_measurement", ["variable"] = "unit"
				}
			});

		Assert.That(((RecordingVariableApi)accessor.Current!).Written["unit"], Is.EqualTo("C"));
	}

	[Test]
	public async Task GetEntityState_fails_with_NotFound_for_a_missing_attribute()
	{
		await ConnectAsync("""[{ "entity_id": "sensor.temp", "state": "21.5", "attributes": {} }]""");

		var accessor = new HomeAssistantVariableAccessor { Current = new RecordingVariableApi() };
		var definition = new GetEntityStateActionDefinition(() => _connection, accessor);
		var result = await definition.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["entity"] = "sensor.temp", ["attribute"] = "battery_level", ["variable"] = "battery"
				}
			});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}


	[Test]
	public void Every_action_parameter_uses_a_defined_parameter_type()
	{
		var definitions = HomeAssistantActions.Create(() => null, new HomeAssistantVariableAccessor());

		Assert.Multiple(() =>
		{
			foreach (var definition in definitions)
			{
				foreach (var parameter in definition.Parameters)
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{definition.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public async Task Entity_options_answer_instantly_and_stay_typeable_without_a_connection()
	{
		var definition = new LightActionDefinition(() => null);
		var options = await definition.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "entity", CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options, Is.Empty);
			Assert.That(options.AllowsCustomValue, Is.True);
			Assert.That(options.CacheSeconds, Is.EqualTo(30));
		});
	}

	private async Task ConnectAsync(string getStatesJson, string? getServicesJson = null)
	{
		_client = new FakeHomeAssistantClient
		{
			Responses =
			{
				["get_states"] = getStatesJson,
				["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }""",
				["get_services"] = getServicesJson ?? "{}",
				["config/area_registry/list"] = "[]",
				["config/device_registry/list"] = "[]",
				["config/entity_registry/list"] = "[]"
			}
		};

		_connection = new HomeAssistantConnection(() => _client!, _uri, "the-token");
		_connection.Start();
		await WaitForAsync(() => _connection.IsConnected, "the connection to come up");
	}

	[Test]
	public async Task An_entity_power_button_reports_whether_the_entity_is_on()
	{
		await ConnectAsync("""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }]""");
		var provider = Provider("turn-on");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>
				{ [EntityPowerActionDefinition.EntitiesParameterName] = _kitchenLight },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
	}

	[Test]
	public async Task An_entity_power_button_targeting_several_entities_is_on_only_when_all_of_them_are()
	{
		await ConnectAsync(
			"""[{ "entity_id": "light.kitchen", "state": "on", "attributes": {} }, { "entity_id": "switch.fan", "state": "off", "attributes": {} }]""");
		var provider = Provider("turn-on");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>
			{
				[EntityPowerActionDefinition.EntitiesParameterName] = _kitchenLightAndFan
			},
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("off"));
	}

	[Test]
	public async Task An_entity_home_assistant_does_not_know_is_unavailable_rather_than_off()
	{
		await ConnectAsync("[]");
		var provider = Provider("turn-on");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>
				{ [EntityPowerActionDefinition.EntitiesParameterName] = _missingEntity },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task An_entity_power_button_with_nothing_selected_yet_reports_no_state_at_all()
	{
		await ConnectAsync("[]");
		var provider = Provider("turn-on");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public async Task An_automation_button_reports_whether_the_automation_is_enabled()
	{
		await ConnectAsync("""[{ "entity_id": "automation.lights_out", "state": "on", "attributes": {} }]""");
		var provider = Provider("automation-control");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>
				{ [AutomationActionDefinition.EntityParameterName] = "automation.lights_out" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
	}

	private static readonly string[] _kitchenLight = ["light.kitchen"];
	private static readonly string[] _kitchenLightAndFan = ["light.kitchen", "switch.fan"];
	private static readonly string[] _missingEntity = ["light.gone"];

	private IStateProviderActionDefinition Provider(string actionId)
		=> (IStateProviderActionDefinition)HomeAssistantActions
			.Create(() => _connection, new HomeAssistantVariableAccessor())
			.Single(action => action.Id == actionId);

	private static async Task WaitForAsync(Func<bool> condition, string because)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

	private sealed class RecordingVariableApi : MacroDeck.Sdk.Variables.IVariableApi
	{
		private readonly Dictionary<Guid, string> _names = [];

		public Dictionary<string, object?> Written { get; } = new(StringComparer.Ordinal);

		public Task<IReadOnlyList<MacroDeck.Sdk.Variables.VariableHandle>> GetAllAsync()
			=> Task.FromResult<IReadOnlyList<MacroDeck.Sdk.Variables.VariableHandle>>([]);

		public Task<MacroDeck.Sdk.Variables.VariableHandle?> GetByNameAsync(string name)
			=> Task.FromResult<MacroDeck.Sdk.Variables.VariableHandle?>(null);

		public Task<MacroDeck.Sdk.Variables.VariableHandle> CreateAsync(
			string name,
			MacroDeck.Sdk.Variables.VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			var id = Guid.NewGuid();
			_names[id] = name;
			Written[name] = initialValue;
			return Task.FromResult(
				new MacroDeck.Sdk.Variables.VariableHandle(id, name, type, initialValue, decimalPlaces)
				{
					DefinitionId = definitionId
				});
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			if (_names.TryGetValue(variableId, out var name))
			{
				Written[name] = value;
			}

			return Task.CompletedTask;
		}

		public Task DeleteAsync(Guid variableId) => Task.CompletedTask;
	}
}
