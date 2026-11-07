using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Http;
using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class JsonEditActionsTests
{
	private const string Target = "target";

	private ActionVariableTargets _targets = null!;
	private HttpVariableAccessor _accessor = null!;

	[SetUp]
	public void SetUp()
	{
		_targets = new ActionVariableTargets(HttpIntegration.IntegrationId);
		_accessor = new HttpVariableAccessor
		{
			Current = _targets.IntegrationVariables,
			UserVariables = _targets.UserVariables
		};
	}

	[TearDown]
	public void TearDown()
	{
		_targets.Dispose();
	}

	[Test]
	public async Task Set_builds_a_request_body_from_several_values_and_creates_missing_objects()
	{
		var first = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", "{}"),
			("path", "user.name"),
			("valueType", "text"),
			("value", "Ada"));
		var second = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", (string)(await _targets.ValueOf(Target))!),
			("path", "$.user.age"),
			("valueType", "number"),
			("value", "36"));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(first.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(second.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("""{"user":{"name":"Ada","age":36}}"""));
		});
	}

	[TestCase("text", " spaced ", """{"v":" spaced "}""")]
	[TestCase("text", "", """{"v":""}""")]
	[TestCase("number", "-1.5e3", """{"v":-1.5e3}""")]
	[TestCase("number", "12345678901234567891", """{"v":12345678901234567891}""")]
	[TestCase("number", "1e400", """{"v":1e400}""")]
	[TestCase("boolean", "true", """{"v":true}""")]
	[TestCase("boolean", "False", """{"v":false}""")]
	[TestCase("null", "ignored", """{"v":null}""")]
	[TestCase("json", """{"a":[1,"ü"]}""", """{"v":{"a":[1,"ü"]}}""")]
	public async Task Set_writes_each_value_type(string valueType, string value, string expected)
	{
		var result = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", "{}"),
			("path", "v"),
			("valueType", valueType),
			("value", value));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task Set_replaces_an_existing_array_item_and_the_whole_document()
	{
		var item = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", """{"list":[1,2,3]}"""),
			("path", "list[1]"),
			("valueType", "number"),
			("value", "9"));
		var itemResult = await _targets.ValueOf(Target);
		var root = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", """{"list":[1]}"""),
			("path", "$"),
			("valueType", "json"),
			("value", "[]"));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(item.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(itemResult, Is.EqualTo("""{"list":[1,9,3]}"""));
			Assert.That(root.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("[]"));
		});
	}

	[Test]
	public async Task A_value_written_by_set_is_read_back_by_get_json_value_with_the_same_path()
	{
		const string path = "data.items[0].name";
		await Run(new SetJsonValueActionDefinition(_accessor),
			("json", """{"data":{"items":[{"name":"old"}]}}"""),
			("path", path),
			("valueType", "text"),
			("value", "new"));

		var json = (string)(await _targets.ValueOf(Target))!;

		Assert.That(JsonPathExtractor.TryExtract(json, path, out _, out var value), Is.True);
		Assert.That(value, Is.EqualTo("new"));
	}

	[Test]
	public async Task Remove_removes_a_property_and_an_array_item()
	{
		var property = await Run(new RemoveJsonValueActionDefinition(_accessor),
			("json", """{"a":1,"b":2}"""),
			("path", "a"));
		var propertyResult = await _targets.ValueOf(Target);
		var item = await Run(new RemoveJsonValueActionDefinition(_accessor),
			("json", """{"queue":["x","y","z"]}"""),
			("path", "queue[0]"));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(property.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(propertyResult, Is.EqualTo("""{"b":2}"""));
			Assert.That(item.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("""{"queue":["y","z"]}"""));
		});
	}

	[TestCase("""{"results":[1,2,3]}""", "results", 3d)]
	[TestCase("""{"a":1,"b":2}""", "$", 2d)]
	[TestCase("[]", "", 0d)]
	public async Task Count_writes_the_number_of_items_or_properties(string json, string path, double expected)
	{
		var result = await Run(new CountJsonItemsActionDefinition(_accessor), ("json", json), ("path", path));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task Append_adds_to_the_end_of_an_existing_array_and_creates_a_missing_one()
	{
		var existing = await Run(new AppendJsonArrayItemActionDefinition(_accessor),
			("json", """{"queue":["a"]}"""),
			("path", "queue"),
			("valueType", "text"),
			("value", "b"));
		var existingResult = await _targets.ValueOf(Target);
		var missing = await Run(new AppendJsonArrayItemActionDefinition(_accessor),
			("json", "{}"),
			("path", "state.queue"),
			("valueType", "number"),
			("value", "1"));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(existing.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(existingResult, Is.EqualTo("""{"queue":["a","b"]}"""));
			Assert.That(missing.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("""{"state":{"queue":[1]}}"""));
		});
	}

	[Test]
	public async Task Append_to_the_root_array()
	{
		var result = await Run(new AppendJsonArrayItemActionDefinition(_accessor),
			("json", "[1]"),
			("path", "$"),
			("valueType", "json"),
			("value", """{"id":2}"""));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("""[1,{"id":2}]"""));
		});
	}

	[TestCase("not json")]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("{\"a\":")]
	public async Task Invalid_json_fails_every_action_and_leaves_the_variable_untouched(string json)
	{
		await AssertFailsForEveryAction(ActionErrorCodes.InvalidParameter, ("json", json), ("path", "a"));
	}

	[TestCase("a..b")]
	[TestCase("a[0")]
	[TestCase("a[x]")]
	[TestCase("a[-1]")]
	[TestCase("a[0]b")]
	[TestCase("a[99999999999]")]
	public async Task A_malformed_path_fails_every_action_and_leaves_the_variable_untouched(string path)
	{
		await AssertFailsForEveryAction(ActionErrorCodes.InvalidParameter, ("json", """{"a":[]}"""), ("path", path));
	}

	[Test]
	public async Task A_blank_path_is_invalid_except_for_count()
	{
		await _targets.CreateUserVariable(Target, DomainVariableType.Text, "keep");
		(string Name, object Value)[] parameters =
			[("json", "[]"), ("path", " "), ("valueType", "number"), ("value", "1")];

		var set = await Run(new SetJsonValueActionDefinition(_accessor), parameters);
		var remove = await Run(new RemoveJsonValueActionDefinition(_accessor), parameters);
		var append = await Run(new AppendJsonArrayItemActionDefinition(_accessor), parameters);
		var count = await Run(new CountJsonItemsActionDefinition(_accessor), ("json", "[1,2]"), ("path", " "));

		Assert.Multiple(() =>
		{
			Assert.That(new[] { set.ErrorCode, remove.ErrorCode, append.ErrorCode },
				Is.All.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(count.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task A_missing_path_fails_with_not_found()
	{
		await _targets.CreateUserVariable(Target, DomainVariableType.Text, "keep");

		var remove = await Run(new RemoveJsonValueActionDefinition(_accessor),
			("json", """{"a":1}"""),
			("path", "b"));
		var count = await Run(new CountJsonItemsActionDefinition(_accessor),
			("json", """{"a":1}"""),
			("path", "b.c"));
		var setIndex = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", """{"list":[]}"""),
			("path", "list[0]"),
			("valueType", "number"),
			("value", "1"));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(new[] { remove.ErrorCode, count.ErrorCode, setIndex.ErrorCode },
				Is.All.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("keep"));
		});
	}

	[TestCase("""{"settings":null}""", "settings.theme")]
	[TestCase("""{"settings":"dark"}""", "settings.theme")]
	[TestCase("""{"settings":{}}""", "settings[0]")]
	[TestCase("""{"list":[1]}""", "list.name")]
	public async Task Set_never_overwrites_a_value_on_the_way(string json, string path)
	{
		await _targets.CreateUserVariable(Target, DomainVariableType.Text, "keep");

		var result = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", json),
			("path", path),
			("valueType", "text"),
			("value", "x"));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("keep"));
		});
	}

	[Test]
	public async Task Count_and_append_reject_a_value_of_the_wrong_kind()
	{
		var count = await Run(new CountJsonItemsActionDefinition(_accessor),
			("json", """{"n":5}"""),
			("path", "n"));
		var append = await Run(new AppendJsonArrayItemActionDefinition(_accessor),
			("json", """{"queue":{}}"""),
			("path", "queue"),
			("valueType", "text"),
			("value", "x"));

		Assert.Multiple(() =>
		{
			Assert.That(count.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(append.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task Removing_the_whole_document_fails()
	{
		var result = await Run(new RemoveJsonValueActionDefinition(_accessor), ("json", "{}"), ("path", "$"));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[TestCase("number", "NaN")]
	[TestCase("number", "Infinity")]
	[TestCase("number", "+1")]
	[TestCase("number", "abc")]
	[TestCase("number", "\"1\"")]
	[TestCase("number", "")]
	[TestCase("boolean", "yes")]
	[TestCase("json", "{broken")]
	[TestCase("json", "")]
	[TestCase("unknown", "x")]
	public async Task A_value_that_does_not_match_its_type_fails_and_leaves_the_variable_untouched(
		string valueType,
		string value)
	{
		await _targets.CreateUserVariable(Target, DomainVariableType.Text, "keep");

		var set = await Run(new SetJsonValueActionDefinition(_accessor),
			("json", "{}"),
			("path", "v"),
			("valueType", valueType),
			("value", value));
		var append = await Run(new AppendJsonArrayItemActionDefinition(_accessor),
			("json", "[]"),
			("path", "$"),
			("valueType", valueType),
			("value", value));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(set.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(append.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("keep"));
		});
	}

	[Test]
	public async Task Duplicate_property_names_never_escape_as_an_exception()
	{
		var result = await Run(new CountJsonItemsActionDefinition(_accessor),
			("json", """{"a":1,"a":2}"""),
			("path", "$"));

		Assert.That(result.Status is ActionResultStatus.Succeeded
			|| result.ErrorCode == ActionErrorCodes.InvalidParameter);
	}

	[Test]
	public void Every_json_action_declares_its_target_variable_from_the_user_variables()
	{
		IActionDefinition[] definitions =
		[
			new SetJsonValueActionDefinition(_accessor),
			new RemoveJsonValueActionDefinition(_accessor),
			new CountJsonItemsActionDefinition(_accessor),
			new AppendJsonArrayItemActionDefinition(_accessor)
		];

		var targets = definitions.Select(definition => definition.Parameters.Single(p => p.Name == "variable"))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(targets.Select(target => target.Required), Is.All.True);
			Assert.That(targets.Select(target => target.OptionsSourceId),
				Is.All.EqualTo(HostOptionsSources.UserVariables));
		});
	}

	private async Task AssertFailsForEveryAction(string code, params (string Name, object Value)[] input)
	{
		await _targets.CreateUserVariable(Target, DomainVariableType.Text, "keep");
		(string Name, object Value)[] parameters = [.. input, ("valueType", "text"), ("value", "x")];

		IActionDefinition[] definitions =
		[
			new SetJsonValueActionDefinition(_accessor),
			new RemoveJsonValueActionDefinition(_accessor),
			new CountJsonItemsActionDefinition(_accessor),
			new AppendJsonArrayItemActionDefinition(_accessor)
		];

		var codes = new List<string?>();
		foreach (var definition in definitions)
		{
			codes.Add((await Run(definition, parameters)).ErrorCode);
		}

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(codes, Is.All.EqualTo(code));
			Assert.That(await _targets.ValueOf(Target), Is.EqualTo("keep"));
		});
	}

	private static Task<ActionResult> Run(IActionDefinition definition, params (string Name, object Value)[] values)
	{
		var parameters = values
			.Append(("variable", Target))
			.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
		return definition.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}
}
