using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;

namespace MacroDeck.Sdk.Tests.UnitTests.Actions;

[TestFixture]
public class CodeArrayAndIssueAdditionsCompatibilityTests
{
	[Test]
	public void Parameters_and_issues_built_the_old_way_keep_the_old_behaviour()
	{
		var code = ActionParameter.Code("script", "lua", "Script");
		var array = ActionParameter.Array("arguments", ActionParameter.Text("argument"), "Arguments");
		var initialized = new ActionParameter { Name = "items", Type = ActionParameterType.Array };
		var issue = new IntegrationIssue { Id = "missing", Title = "Missing", ActionLabel = "Fix" };

		Assert.Multiple(() =>
		{
			Assert.That(code.VariableInsertFormat, Is.Null);
			Assert.That(array.TemplatedItems, Is.False);
			Assert.That(initialized.TemplatedItems, Is.False);
			Assert.That(issue.HelpUrl, Is.Null);
		});
	}

	[Test]
	public void Templated_items_can_also_be_set_in_an_object_initializer()
	{
		var initialized = new ActionParameter
			{ Name = "items", Type = ActionParameterType.Array, TemplatedItems = true };

		Assert.That(initialized.TemplatedItems, Is.True);
	}
}
