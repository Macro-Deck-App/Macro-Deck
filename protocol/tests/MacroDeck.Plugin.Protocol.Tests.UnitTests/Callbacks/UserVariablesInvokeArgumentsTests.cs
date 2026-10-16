using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class UserVariablesInvokeArgumentsTests
{
	/// <summary>
	/// The variable type travels as its enumerant name. Declaring it as the enum itself would serialize it
	/// as an integer and freeze the SDK enum's declaration order into the wire contract.
	/// </summary>
	[Test]
	public void A_create_carries_the_variable_type_as_a_string()
	{
		var arguments = new UserVariablesCreateArguments
		{
			Name = "current_track",
			OwnerWidgetId = "11111111-1111-1111-1111-111111111111",
			Type = "Numeric",
			InitialValue = "1",
			DecimalPlaces = 2
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"type\":\"Numeric\""));
			Assert.That(json, Does.Not.Contain("\"type\":1"));
		});

		var round = JsonSerializer.Deserialize<UserVariablesCreateArguments>(json, PluginProtocolJson.Options);

		Assert.That(round, Is.EqualTo(arguments));
	}

	[Test]
	public void A_create_from_a_peer_that_sends_only_the_required_fields_still_reads()
	{
		const string json = """{"name":"current_track","type":"Text"}""";

		var arguments = JsonSerializer.Deserialize<UserVariablesCreateArguments>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(arguments!.Name, Is.EqualTo("current_track"));
			Assert.That(arguments.OwnerWidgetId, Is.Null);
			Assert.That(arguments.InitialValue, Is.Null);
			Assert.That(arguments.DecimalPlaces, Is.Null);
		});
	}

	[Test]
	public void An_apply_from_a_plugin_that_predates_script_variables_reads_without_them()
	{
		const string json = """{"name":"count","ownerWidgetId":"w1","operation":"Add","value":"1"}""";

		var arguments = JsonSerializer.Deserialize<UserVariablesApplyArguments>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(arguments!.OwnerWidgetId, Is.EqualTo("w1"));
			Assert.That(arguments.ScriptId, Is.Null);
			Assert.That(arguments.AutomationId, Is.Null);
		});
	}

	[Test]
	public void The_script_and_automation_travel_as_optional_camel_case_fields()
	{
		var apply = new UserVariablesApplyArguments
		{
			Name = "count", Operation = "Set", ScriptId = "s1", AutomationId = "a1"
		};
		var create = new UserVariablesCreateArguments { Name = "count", Type = "Text", ScriptId = "s1" };
		var execute = new ActionExecuteArguments { ScriptId = "s1", AutomationId = "a1" };

		var applyJson = JsonSerializer.Serialize(apply, PluginProtocolJson.Options);
		var createJson = JsonSerializer.Serialize(create, PluginProtocolJson.Options);
		var executeJson = JsonSerializer.Serialize(execute, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(applyJson, Does.Contain("\"scriptId\":\"s1\"").And.Contain("\"automationId\":\"a1\""));
			Assert.That(createJson, Does.Contain("\"scriptId\":\"s1\"").And.Not.Contain("automationId"));
			Assert.That(executeJson, Does.Contain("\"scriptId\":\"s1\"").And.Contain("\"automationId\":\"a1\""));
			Assert.That(JsonSerializer.Deserialize<UserVariablesApplyArguments>(applyJson, PluginProtocolJson.Options),
				Is.EqualTo(apply));
		});
	}

	[Test]
	public void An_execute_from_a_host_that_predates_script_variables_reads_without_them()
	{
		const string json = """{"parameters":{},"ownerWidgetId":"w1"}""";

		var arguments = JsonSerializer.Deserialize<ActionExecuteArguments>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(arguments!.OwnerWidgetId, Is.EqualTo("w1"));
			Assert.That(arguments.ScriptId, Is.Null);
			Assert.That(arguments.AutomationId, Is.Null);
		});
	}
}
