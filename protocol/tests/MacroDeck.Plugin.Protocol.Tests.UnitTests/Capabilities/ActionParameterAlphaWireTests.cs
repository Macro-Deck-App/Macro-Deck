using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class ActionParameterAlphaWireTests
{
	[Test]
	public void A_parameter_that_does_not_allow_alpha_writes_no_member_for_it()
	{
		var json = JsonSerializer.Serialize(new ActionParameterDto { Name = "color", Type = "Color" },
			PluginProtocolJson.Options);

		Assert.That(json, Does.Not.Contain("allowAlpha"));
	}

	[Test]
	public void A_parameter_from_an_older_plugin_reads_as_not_allowing_alpha()
	{
		var dto = JsonSerializer.Deserialize<ActionParameterDto>("""{"name":"color","type":"Color","supportsReset":true}""",
			PluginProtocolJson.Options)!;

		Assert.That(dto.AllowAlpha, Is.Null);
	}

	[Test]
	public void A_parameter_that_allows_alpha_round_trips()
	{
		var json = JsonSerializer.Serialize(new ActionParameterDto { Name = "color", Type = "Color", AllowAlpha = true },
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"allowAlpha\":true"));
			Assert.That(JsonSerializer.Deserialize<ActionParameterDto>(json, PluginProtocolJson.Options)!.AllowAlpha,
				Is.True);
		});
	}
}
