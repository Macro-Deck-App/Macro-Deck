using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class ActionParameterCodeAndArrayWireTests
{
	[Test]
	public void A_parameter_without_the_new_fields_writes_what_it_wrote_before()
	{
		var json = JsonSerializer.Serialize(new ActionParameterDto { Name = "script", Type = "Code" },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Not.Contain("variableInsertFormat"));
			Assert.That(json, Does.Not.Contain("templatedItems"));
		});
	}

	[Test]
	public void A_parameter_from_an_older_plugin_has_no_insert_format_and_untemplated_items()
	{
		var dto = JsonSerializer.Deserialize<ActionParameterDto>(
			"""{"name":"arguments","type":"Array","itemTemplate":{"name":"argument","type":"String"}}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(dto.VariableInsertFormat, Is.Null);
			Assert.That(dto.TemplatedItems, Is.Null);
		});
	}

	[Test]
	public void The_insert_format_and_templated_items_round_trip()
	{
		var json = JsonSerializer.Serialize(new ActionParameterDto
			{
				Name = "script",
				Type = "Code",
				Language = "autohotkey",
				VariableInsertFormat = "MacroDeck.GetVar(\"{name}\")",
				TemplatedItems = true
			},
			PluginProtocolJson.Options);
		var dto = JsonSerializer.Deserialize<ActionParameterDto>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"templatedItems\":true"));
			Assert.That(dto.VariableInsertFormat, Is.EqualTo("MacroDeck.GetVar(\"{name}\")"));
			Assert.That(dto.TemplatedItems, Is.True);
		});
	}
}
