using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Ui;
using ActionParameterType = MacroDeck.Sdk.Actions.ActionParameterType;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class ActionParameterCodeAndArrayMappingTests
{
	[Test]
	public void A_parameter_from_a_plugin_that_predates_both_fields_has_no_insert_format_and_untemplated_items()
	{
		var parameter = ActionParameterMapper.ToDomain(new ActionParameterDto
		{
			Name = "arguments",
			Type = "Array",
			ItemTemplate = new ActionParameterDto { Name = "argument", Type = "String" }
		});

		Assert.Multiple(() =>
		{
			Assert.That(parameter.VariableInsertFormat, Is.Null);
			Assert.That(parameter.TemplatedItems, Is.False);
		});
	}

	[Test]
	public void A_plugin_parameter_keeps_its_insert_format_and_templated_items_both_ways()
	{
		var declared = new ActionParameter
			{
				Name = "arguments",
				Type = ActionParameterType.Array,
				ItemTemplate = ActionParameter.Code("argument", "autohotkey"),
				VariableInsertFormat = "%{name}%"
			}.WithTemplatedItems()
			.OnlyWhen("mode", "file");

		var restored = ActionParameterMapper.ToDomain(ActionParameterMapper.ToDto(declared));

		Assert.Multiple(() =>
		{
			Assert.That(restored.VariableInsertFormat, Is.EqualTo("%{name}%"));
			Assert.That(restored.TemplatedItems, Is.True);
			Assert.That(restored.VisibleWhen?.ParameterName, Is.EqualTo("mode"));
		});
	}

	[Test]
	public void The_editor_sees_the_insert_format_only_on_a_parameter_that_declares_one()
	{
		var plain = JsonSerializer.Serialize(ActionParameterDefMapper.Map(ActionParameter.Code("script", "lua")),
			UiWebSocketProtocol.Json);
		var withFormat = JsonSerializer.Serialize(ActionParameterDefMapper.Map(new ActionParameter
			{
				Name = "script",
				Type = ActionParameterType.Code,
				Language = "autohotkey",
				VariableInsertFormat = "%{name}%"
			}),
			UiWebSocketProtocol.Json);

		Assert.Multiple(() =>
		{
			Assert.That(plain, Does.Not.Contain("variableInsertFormat"));
			Assert.That(withFormat, Does.Contain("\"variableInsertFormat\":\"%{name}%\""));
		});
	}

	[Test]
	public void An_issue_from_a_plugin_keeps_an_absolute_help_link_and_one_that_predates_it_has_none()
	{
		var old = IssueMapper.ToDomain(new IntegrationIssueDescriptorDto { Id = "a", Title = "A", Severity = "Error" });
		var linked = IssueMapper.ToDomain(new IntegrationIssueDescriptorDto
			{ Id = "a", Title = "A", Severity = "Error", HelpUrl = "https://example.com/help" });
		var relative = IssueMapper.ToDomain(new IntegrationIssueDescriptorDto
			{ Id = "a", Title = "A", Severity = "Error", HelpUrl = "help.html" });

		Assert.Multiple(() =>
		{
			Assert.That(old.HelpUrl, Is.Null);
			Assert.That(linked.HelpUrl, Is.EqualTo(new Uri("https://example.com/help")));
			Assert.That(relative.HelpUrl, Is.Null);
		});
	}
}
