using System.Globalization;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsInputSettingsTests
{
	private const string ActionId = "set-input-setting";

	private static (ObsConnection Connection, FakeObsClient Client) Create(bool connected = true)
	{
		var client = new FakeObsClient { IsConnected = connected };
		return (new ObsConnection(client, "ws://localhost:4455", null), client);
	}

	private static IActionDefinition Action(ObsConnection connection)
		=> ObsActions.Create(() => connection, new VariableApiAccessor()).Single(a => a.Id == ActionId);

	private static Task<ActionResult> RunAsync(ObsConnection connection, string? input, string? setting, object? value)
	{
		var parameters = new Dictionary<string, object>();
		if (input is not null)
		{
			parameters[SetInputSettingActionDefinition.InputParameter] = input;
		}

		if (setting is not null)
		{
			parameters[SetInputSettingActionDefinition.SettingParameter] = setting;
		}

		if (value is not null)
		{
			parameters[SetInputSettingActionDefinition.ValueParameter] = value;
		}

		return Action(connection).CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private static JsonNode? Sent(FakeObsClient client, string input)
	{
		var prefix = $"SetInputSettings:{input}:";
		var call = client.Calls.Single(c => c.StartsWith(prefix, StringComparison.Ordinal));
		return JsonNode.Parse(call[prefix.Length..]);
	}

	private static bool SentAnything(FakeObsClient client)
		=> client.Calls.Any(c => c.StartsWith("SetInputSettings:", StringComparison.Ordinal));

	[Test]
	public async Task Text_of_a_text_source_reaches_obs_verbatim_and_alone()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"old","font":{"face":"Arial","size":72}}""";

		var result = await RunAsync(connection, "Title", "text", "Now live: 2026-10-06T12:00:00.000Z");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(JsonNode.DeepEquals(Sent(client, "Title"),
				JsonNode.Parse("""{"text":"Now live: 2026-10-06T12:00:00.000Z"}""")), Is.True);
		});
	}

	[TestCase("""{"opacity":50}""", "75", """{"opacity":75}""")]
	[TestCase("""{"opacity":50}""", " 0.5 ", """{"opacity":0.5}""")]
	[TestCase("""{"opacity":50}""", "1e2", """{"opacity":100}""")]
	[TestCase("""{"looping":false}""", "TRUE", """{"looping":true}""")]
	[TestCase("""{"label":"7"}""", "42", """{"label":"42"}""")]
	[TestCase("""{"font":{"face":"Arial"}}""", """{"face":"Comic Sans","size":40}""",
		"""{"font":{"face":"Comic Sans","size":40}}""")]
	public async Task Value_takes_the_type_of_the_current_setting(string current, string value, string expected)
	{
		var (connection, client) = Create();
		using var _ = connection;
		var key = JsonNode.Parse(expected)!.AsObject().Single().Key;
		client.InputSettingsJson["Source"] = current;

		var result = await RunAsync(connection, "Source", key, value);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(JsonNode.DeepEquals(Sent(client, "Source"), JsonNode.Parse(expected)), Is.True);
		});
	}

	[Test]
	public async Task A_setting_obs_left_at_its_default_is_typed_from_the_defaults()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"hi"}""";
		client.InputDefaultSettingsJson["Title"] = """{"opacity":100,"outline":false}""";

		var opacity = await RunAsync(connection, "Title", "opacity", "40");

		Assert.Multiple(() =>
		{
			Assert.That(opacity.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(JsonNode.DeepEquals(Sent(client, "Title"), JsonNode.Parse("""{"opacity":40}""")), Is.True);
		});
	}

	[Test]
	public async Task A_setting_obs_knows_nothing_about_is_sent_as_text()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Browser"] = "{}";
		client.InputDefaultSettingsJson["Browser"] = """{"width":800}""";

		var result = await RunAsync(connection, "Browser", " url ", "https://example.com/?a=1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(JsonNode.DeepEquals(Sent(client, "Browser"),
				JsonNode.Parse("""{"url":"https://example.com/?a=1"}""")), Is.True);
		});
	}

	[Test]
	public async Task An_empty_value_clears_a_text_setting()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"old"}""";

		var result = await RunAsync(connection, "Title", "text", null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(JsonNode.DeepEquals(Sent(client, "Title"), JsonNode.Parse("""{"text":""}""")), Is.True);
		});
	}

	[Test]
	public async Task Non_text_parameter_values_are_written_independent_of_the_host_culture()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
		try
		{
			var (connection, client) = Create();
			using var _ = connection;
			client.InputSettingsJson["Source"] = """{"opacity":1,"looping":false,"label":"x"}""";

			var results = new[]
			{
				await RunAsync(connection, "Source", "opacity", 0.5d),
				await RunAsync(connection, "Source", "looping", true),
				await RunAsync(connection, "Source", "label", 1234.5d)
			};

			var sent = client.Calls.Where(c => c.StartsWith("SetInputSettings:", StringComparison.Ordinal))
				.Select(c => JsonNode.Parse(c["SetInputSettings:Source:".Length..]))
				.ToList();
			Assert.Multiple(() =>
			{
				Assert.That(results.Select(r => r.Status), Is.All.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(JsonNode.DeepEquals(sent[0], JsonNode.Parse("""{"opacity":0.5}""")), Is.True);
				Assert.That(JsonNode.DeepEquals(sent[1], JsonNode.Parse("""{"looping":true}""")), Is.True);
				Assert.That(JsonNode.DeepEquals(sent[2], JsonNode.Parse("""{"label":"1234.5"}""")), Is.True);
			});
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[TestCase("""{"looping":false}""", "looping", "yes")]
	[TestCase("""{"opacity":50}""", "opacity", "half")]
	[TestCase("""{"opacity":50}""", "opacity", "NaN")]
	[TestCase("""{"opacity":50}""", "opacity", "Infinity")]
	[TestCase("""{"font":{"face":"Arial"}}""", "font", "Arial")]
	[TestCase("""{"font":{"face":"Arial"}}""", "font", "[1,2]")]
	[TestCase("""{"playlist":[]}""", "playlist", "{\"a\":1}")]
	public async Task A_value_that_does_not_fit_the_setting_is_rejected_and_nothing_is_sent(
		string current,
		string setting,
		string value)
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Source"] = current;

		var result = await RunAsync(connection, "Source", setting, value);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(SentAnything(client), Is.False);
		});
	}

	[TestCase(null, "text")]
	[TestCase(" ", "text")]
	[TestCase("Title", null)]
	[TestCase("Title", "  ")]
	public async Task Input_and_setting_are_required(string? input, string? setting)
	{
		var (connection, client) = Create();
		using var _ = connection;

		var result = await RunAsync(connection, input, setting, "x");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(SentAnything(client), Is.False);
		});
	}

	[Test]
	public async Task An_input_whose_settings_cannot_be_read_is_reported_missing_and_nothing_is_sent()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.DeletedNames.Add("Gone");

		var result = await RunAsync(connection, "Gone", "text", "x");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(SentAnything(client), Is.False);
		});
	}

	[Test]
	public async Task A_failed_defaults_read_never_guesses_the_type()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"hi"}""";
		client.DefaultSettingsFailure = new InvalidOperationException("boom");

		var result = await RunAsync(connection, "Title", "opacity", "50");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(SentAnything(client), Is.False);
		});
	}

	[Test]
	public async Task Obs_dropping_during_the_press_is_reported_as_not_connected()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"hi"}""";
		client.DisconnectOnDefaultSettingsRead = true;

		var result = await RunAsync(connection, "Title", "opacity", "50");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(SentAnything(client), Is.False);
		});
	}

	[Test]
	public async Task Offline_obs_fails_with_not_connected_and_nothing_is_sent()
	{
		var (connection, client) = Create(connected: false);
		using var _ = connection;

		var result = await RunAsync(connection, "Title", "text", "x");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_request_obs_refuses_fails_with_its_reason()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"hi"}""";
		client.RequestFailure = new ObsRequestException(400, "Invalid settings");

		var result = await RunAsync(connection, "Title", "text", "x");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(result.ErrorMessage.Localized?.Arguments["message"]?.ToString(), Is.EqualTo("Invalid settings"));
		});
	}

	[Test]
	public async Task Setting_suggestions_list_current_and_default_keys_of_the_chosen_input_and_follow_the_filter()
	{
		var (connection, client) = Create();
		using var _ = connection;
		client.InputSettingsJson["Title"] = """{"text":"hi","font":{}}""";
		client.InputDefaultSettingsJson["Title"] = """{"opacity":100,"font":{},"outline":false}""";
		var action = (IDynamicOptionsActionDefinition)Action(connection);

		async Task<IEnumerable<string>> OptionsAsync(string? input, string? filter)
		{
			var parameters = new Dictionary<string, object?>();
			if (input is not null)
			{
				parameters[SetInputSettingActionDefinition.InputParameter] = input;
			}

			var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = SetInputSettingActionDefinition.SettingParameter,
					CurrentParameters = parameters,
					Filter = filter
				},
				CancellationToken.None);
			Assert.That(result.Options.All(o => o.Label.ToString() == o.Value), Is.True);
			return result.Options.Select(o => o.Value);
		}

		Assert.Multiple(async () =>
		{
			Assert.That(await OptionsAsync("Title", null), Is.EqualTo(new[] { "font", "opacity", "outline", "text" }));
			Assert.That(await OptionsAsync("Title", "OU"), Is.EqualTo(new[] { "outline" }));
			Assert.That(await OptionsAsync(null, null), Is.Empty);
		});
	}

	[TestCase("""{"text":"2026-10-06T12:00:00.000+02:00"}""")]
	[TestCase("""{"opacity":0.1}""")]
	[TestCase("""{"seed":9223372036854775807}""")]
	public void Settings_reach_the_obs_library_unchanged(string json)
	{
		var parsed = ObsClient.ParseSettings(json);

		Assert.That(JsonNode.DeepEquals(JsonNode.Parse(parsed.ToString(Newtonsoft.Json.Formatting.None)),
			JsonNode.Parse(json)), Is.True);
	}
}
