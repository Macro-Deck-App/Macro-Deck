using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class ActionParameterSearchableMappingTests
{
	[Test]
	public void A_choice_from_a_plugin_that_predates_search_is_not_searchable()
	{
		var parameter = ActionParameterMapper.ToDomain(new ActionParameterDto
			{ Name = "counter", Type = "DynamicChoice" });

		Assert.That(parameter.Searchable, Is.False);
	}

	[Test]
	public void A_searchable_plugin_parameter_keeps_it_both_ways()
	{
		var declared = ActionParameter.DynamicChoice("counter").AsSearchable();

		var dto = ActionParameterMapper.ToDto(declared);

		Assert.Multiple(() =>
		{
			Assert.That(dto.Searchable, Is.True);
			Assert.That(ActionParameterMapper.ToDomain(dto).Searchable, Is.True);
		});
	}

	[Test]
	public void The_editor_sees_searchable_only_on_a_parameter_that_opted_in()
	{
		var plain = JsonSerializer.Serialize(ActionParameterDefMapper.Map(ActionParameter.DynamicChoice("counter")),
			UiWebSocketProtocol.Json);
		var optedIn = JsonSerializer.Serialize(
			ActionParameterDefMapper.Map(ActionParameter.DynamicChoice("counter").AsSearchable()),
			UiWebSocketProtocol.Json);

		Assert.Multiple(() =>
		{
			Assert.That(plain, Does.Not.Contain("searchable"));
			Assert.That(optedIn, Does.Contain("\"searchable\":true"));
		});
	}
}
