using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using ActionParameterType = MacroDeck.Sdk.Actions.ActionParameterType;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class ActionParameterAlphaMappingTests
{
	[Test]
	public void A_colour_parameter_from_a_plugin_that_predates_alpha_does_not_allow_it()
	{
		var parameter = ActionParameterMapper.ToDomain(new ActionParameterDto { Name = "color", Type = "Color" });

		Assert.That(parameter.AllowAlpha, Is.False);
	}

	[Test]
	public void A_plugin_parameter_that_allows_alpha_keeps_it_both_ways()
	{
		var declared = new ActionParameter { Name = "color", Type = ActionParameterType.Color, AllowAlpha = true };

		var dto = ActionParameterMapper.ToDto(declared);

		Assert.Multiple(() =>
		{
			Assert.That(dto.AllowAlpha, Is.True);
			Assert.That(ActionParameterMapper.ToDomain(dto).AllowAlpha, Is.True);
		});
	}

	[Test]
	public void The_editor_sees_allow_alpha_only_on_a_parameter_that_opted_in()
	{
		var plain = JsonSerializer.Serialize(ActionParameterDefMapper.Map(ActionParameter.Color("color")),
			UiWebSocketProtocol.Json);
		var optedIn = JsonSerializer.Serialize(ActionParameterDefMapper.Map(
				new ActionParameter { Name = "color", Type = ActionParameterType.Color, AllowAlpha = true }),
			UiWebSocketProtocol.Json);

		Assert.Multiple(() =>
		{
			Assert.That(plain, Does.Not.Contain("allowAlpha"));
			Assert.That(optedIn, Does.Contain("\"allowAlpha\":true"));
		});
	}
}
