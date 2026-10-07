using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class ColorVariableWireTests
{
	[Test]
	public void A_color_value_reaches_a_plugin_as_text()
	{
		var dto = VariableValueMapper.ToDto(VariableValueSerializer.Deserialize(VariableType.Color, "#3366ffcc"));

		Assert.Multiple(() =>
		{
			Assert.That(dto.Kind, Is.EqualTo("text"));
			Assert.That(dto.Text, Is.EqualTo("#3366ffcc"));
		});
	}

	[Test]
	public void A_plugins_text_value_is_stored_as_a_canonical_colour()
	{
		var value = VariableValueMapper.ToDomain(new VariableValueDto { Kind = "text", Text = "#ABC" });

		Assert.That(VariableValueSerializer.Serialize(VariableType.Color, value, null), Is.EqualTo("#aabbcc"));
	}
}
