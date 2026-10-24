using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Capabilities.Issues;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ActionParameterCodeAndArrayTests
{
	[Test]
	public void A_parameter_that_sets_neither_flag_sends_neither()
	{
		var code = ActionParameterMapper.ToDto(ActionParameter.Code("script", "lua"));
		var array = ActionParameterMapper.ToDto(ActionParameter.Array("arguments", ActionParameter.Text("argument")));

		Assert.Multiple(() =>
		{
			Assert.That(code.VariableInsertFormat, Is.Null);
			Assert.That(array.TemplatedItems, Is.Null);
		});
	}

	[Test]
	public void A_code_insert_format_and_templated_array_items_reach_the_wire()
	{
		var code = ActionParameterMapper.ToDto(new ActionParameter
		{
			Name = "script",
			Type = ActionParameterType.Code,
			Language = "autohotkey",
			VariableInsertFormat = "MacroDeck.GetVar(\"{name}\")"
		});
		var array = ActionParameterMapper.ToDto(ActionParameter.Array("arguments", ActionParameter.Text("argument"))
			.WithTemplatedItems());

		Assert.Multiple(() =>
		{
			Assert.That(code.VariableInsertFormat, Is.EqualTo("MacroDeck.GetVar(\"{name}\")"));
			Assert.That(array.TemplatedItems, Is.True);
		});
	}

	[Test]
	public void Opting_into_templated_items_keeps_everything_else_and_leaves_the_original_untouched()
	{
		var original = ActionParameter.Array("arguments", ActionParameter.Text("argument"), "Arguments")
			.OnlyWhen("mode", "file");

		var templated = original.WithTemplatedItems();

		Assert.Multiple(() =>
		{
			Assert.That(templated.TemplatedItems, Is.True);
			Assert.That(original.TemplatedItems, Is.False);
			Assert.That(templated.ItemTemplate?.Name, Is.EqualTo("argument"));
			Assert.That(templated.VisibleWhen?.ParameterName, Is.EqualTo("mode"));
		});
	}

	[Test]
	public void An_issue_help_link_reaches_the_wire_and_an_issue_without_one_sends_none()
	{
		var plain = IssueDescriptorMapper.ToDto(new IntegrationIssue { Id = "a", Title = "A" });
		var linked = IssueDescriptorMapper.ToDto(new IntegrationIssue
			{ Id = "a", Title = "A", HelpUrl = new Uri("https://example.com/help") });

		Assert.Multiple(() =>
		{
			Assert.That(plain.HelpUrl, Is.Null);
			Assert.That(linked.HelpUrl, Is.EqualTo("https://example.com/help"));
		});
	}
}
