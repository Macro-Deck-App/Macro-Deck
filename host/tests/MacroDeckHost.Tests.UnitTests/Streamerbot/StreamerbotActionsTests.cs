using System.Text.Json;
using MacroDeckHost.Integrations.Streamerbot;
using MacroDeckHost.Integrations.Streamerbot.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotActionsTests
{
	[Test]
	public void ReadText_trims_and_treats_blank_as_missing()
	{
		var parameters = Parameters(("a", "  Shoutout  "), ("b", "   "));

		Assert.Multiple(() =>
		{
			Assert.That(StreamerbotActionValues.ReadText(parameters, "a"), Is.EqualTo("Shoutout"));
			Assert.That(StreamerbotActionValues.ReadText(parameters, "b"), Is.Null);
			Assert.That(StreamerbotActionValues.ReadText(parameters, "missing"), Is.Null);
		});
	}

	[Test]
	public void ReadText_tolerates_a_null_value()
	{
		var parameters = new Dictionary<string, object> { ["action"] = null! };

		Assert.That(StreamerbotActionValues.ReadText(parameters, "action"), Is.Null);
	}

	[Test]
	public void ReadText_formats_numbers_invariant()
	{
		var parameters = Parameters(("value", 1.5d));

		Assert.That(StreamerbotActionValues.ReadText(parameters, "value"), Is.EqualTo("1.5"));
	}

	[Test]
	public void ReadBool_reads_both_the_toggle_and_its_rendered_text()
	{
		var parameters = Parameters(("toggle", true), ("text", "true"), ("blank", ""));

		Assert.Multiple(() =>
		{
			Assert.That(StreamerbotActionValues.ReadBool(parameters, "toggle"), Is.True);
			Assert.That(StreamerbotActionValues.ReadBool(parameters, "text"), Is.True);
			Assert.That(StreamerbotActionValues.ReadBool(parameters, "blank", fallback: true), Is.True);
			Assert.That(StreamerbotActionValues.ReadBool(parameters, "missing", fallback: true), Is.True);
		});
	}

	[Test]
	public void ReadArguments_reads_the_key_value_editors_string_map()
	{
		var parameters = Parameters(("args", new Dictionary<string, string> { ["target"] = "ada" }));

		var arguments = StreamerbotActionValues.ReadArguments(parameters, "args");

		Assert.That(arguments?["target"], Is.EqualTo("ada"));
	}

	[Test]
	public void ReadArguments_reads_a_json_object_built_by_a_flow()
	{
		var parameters = Parameters(("args", """{ "target": "ada", "count": 3, "flag": true }"""));

		var arguments = StreamerbotActionValues.ReadArguments(parameters, "args");

		Assert.Multiple(() =>
		{
			Assert.That(arguments?["target"], Is.EqualTo("ada"));
			Assert.That(arguments?["count"], Is.EqualTo(3L));
			Assert.That(arguments?["flag"], Is.EqualTo(true));
		});
	}

	[Test]
	public void ReadArguments_answers_null_when_there_is_nothing_to_send()
	{
		var parameters = Parameters(("empty", new Dictionary<string, string>()), ("broken", "not json"));

		Assert.Multiple(() =>
		{
			Assert.That(StreamerbotActionValues.ReadArguments(parameters, "empty"), Is.Null);
			Assert.That(StreamerbotActionValues.ReadArguments(parameters, "broken"), Is.Null);
			Assert.That(StreamerbotActionValues.ReadArguments(parameters, "missing"), Is.Null);
		});
	}

	[Test]
	public void A_global_variable_keeps_the_type_streamerbot_reports()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Convert("7"), Is.EqualTo((VariableType.Numeric, (object)7d)));
			Assert.That(Convert("true"), Is.EqualTo((VariableType.Boolean, (object)true)));
			Assert.That(Convert("\"ada\""), Is.EqualTo((VariableType.Text, (object)"ada")));
			Assert.That(Convert("null"), Is.EqualTo((VariableType.Text, (object)string.Empty)));
		});
	}

	[Test]
	public async Task Running_an_action_without_a_connection_is_a_no_op()
	{
		var definition = new DoActionActionDefinition(() => null);
		var executor = definition.CreateExecutor();

		var result = await executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["action"] = "Shoutout" }
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
		});
	}

	[Test]
	public async Task Running_an_action_without_a_selection_is_a_no_op()
	{
		using var connection = new StreamerbotConnection(() => new FakeStreamerbotClient(),
			new Uri("ws://127.0.0.1:8080/"),
			null);

		var executor = new DoActionActionDefinition(() => connection).CreateExecutor();

		var result = await executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>()
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public void Every_action_parameter_uses_a_defined_parameter_type()
	{
		var integration = new StreamerbotIntegration();

		Assert.Multiple(() =>
		{
			foreach (var action in integration.Actions)
			{
				foreach (var parameter in action.Parameters)
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{action.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public async Task The_option_lists_are_answered_from_the_cache_without_a_connection()
	{
		var doAction = new DoActionActionDefinition(() => null);
		var codeTrigger = new ExecuteCodeTriggerActionDefinition(() => null);
		var context = new DynamicOptionsContext
		{
			ParameterName = "action",
			CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
		};

		var actions = await doAction.GetDynamicOptionsAsync(context, CancellationToken.None);
		var triggers = await codeTrigger.GetDynamicOptionsAsync(context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(actions.Options, Is.Empty);
			Assert.That(actions.AllowsCustomValue, Is.True);
			Assert.That(triggers.Options, Is.Empty);
			Assert.That(triggers.AllowsCustomValue, Is.True);
		});
	}

	private static (VariableType Type, object Value) Convert(string json)
	{
		using var document = JsonDocument.Parse(json);
		return GetGlobalVariableActionDefinition.Convert(document.RootElement);
	}

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
