using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class ActionParameterSearchableWireTests
{
	[Test]
	public void A_parameter_that_is_not_searchable_writes_no_member_for_it()
	{
		var json = JsonSerializer.Serialize(new ActionParameterDto { Name = "counter", Type = "Choice" },
			PluginProtocolJson.Options);

		Assert.That(json, Does.Not.Contain("searchable"));
	}

	[Test]
	public void A_parameter_from_an_older_plugin_reads_as_not_searchable()
	{
		var dto = JsonSerializer.Deserialize<ActionParameterDto>(
			"""{"name":"counter","type":"DynamicChoice","dynamicOptions":true}""",
			PluginProtocolJson.Options)!;

		Assert.That(dto.Searchable, Is.Null);
	}

	[Test]
	public void A_searchable_parameter_round_trips()
	{
		var json = JsonSerializer.Serialize(
			new ActionParameterDto { Name = "counter", Type = "Choice", Searchable = true },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"searchable\":true"));
			Assert.That(JsonSerializer.Deserialize<ActionParameterDto>(json, PluginProtocolJson.Options)!.Searchable,
				Is.True);
		});
	}
}
