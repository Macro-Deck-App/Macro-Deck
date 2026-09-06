using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class WidgetVariableReferenceParserTests
{
	[Test]
	public void LiquidLabelReference_isFound()
	{
		var references = WidgetVariableReferenceParser.Parse("{\"label\":\"{{ vars.cpu }}%\"}");

		Assert.That(references.LabelNames, Is.EquivalentTo(["cpu"]));
	}

	[Test]
	public void ControlFlowOnlyLabelReference_isFound()
	{
		var references = WidgetVariableReferenceParser.Parse("{\"label\":\"{% if vars.cpu %}hot{% endif %}\"}");

		Assert.That(references.LabelNames, Is.EquivalentTo(["cpu"]));
	}

	[Test]
	public void TypedReferenceInStateBinding_isFound()
	{
		var data = "{\"mode\":\"toggle\",\"stateBinding\":{\"kind\":\"compare\"," +
			"\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.Multiple(() =>
		{
			Assert.That(references.StateMappingNames, Is.EquivalentTo(["cpu"]));

			Assert.That(references.LabelNames, Is.Empty);
		});
	}

	[Test]
	public void LiquidOperandInStateBinding_isFound()
	{
		var data = "{\"stateBinding\":{\"kind\":\"compare\"," +
			"\"left\":\"{{ vars.mode }}\",\"operator\":\"==\",\"right\":\"on\"}}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.That(references.StateMappingNames, Does.Contain("mode"));
	}

	[Test]
	public void EscapedNestedFlowJson_isFound()
	{
		var data
			= "{\"flows\":\"[{\\\"blocks\\\":[{\\\"parameters\\\":{\\\"value\\\":{\\\"$var\\\":\\\"cpu\\\"}}}]}]\"," +
			"\"stateBinding\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"}}}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.That(references.StateMappingNames, Does.Contain("cpu"));
	}

	[Test]
	public void LongerNameSharingAPrefix_doesNotMatchTheShorterOne()
	{
		var references = WidgetVariableReferenceParser.Parse("{\"label\":\"{{ vars.cpu_total }}\"}");

		Assert.Multiple(() =>
		{
			Assert.That(references.LabelNames, Is.EquivalentTo(["cpu_total"]));
			Assert.That(references.LabelNames, Does.Not.Contain("cpu"));
		});
	}

	[Test]
	public void NameAppearingOnlyAsAnActionTarget_isNotAStateBindingReference()
	{
		// "Set Variable" naming a target is not a binding operand: the binding does not read it, so a
		// change to it must not queue a re-evaluation.
		var data = "{\"stateBinding\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"}}," +
			"\"flows\":\"[{\\\"action\\\":\\\"set-variable\\\",\\\"target\\\":\\\"counter\\\"}]\"}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.Multiple(() =>
		{
			Assert.That(references.StateMappingNames, Does.Contain("cpu"));
			Assert.That(references.StateMappingNames, Does.Not.Contain("counter"));
		});
	}

	[Test]
	public void PlainLabelWithNeitherFormIsEmpty()
	{
		Assert.That(WidgetVariableReferenceParser.Parse("{\"label\":\"static\"}").IsEmpty, Is.True);
	}

	[Test]
	public void LiquidWithoutAVariableReferenceIsEmpty()
	{
		Assert.That(WidgetVariableReferenceParser.Parse("{\"label\":\"{{ 1 | plus: 2 }}\"}").IsEmpty, Is.True);
	}

	[TestCase(null)]
	[TestCase("")]
	public void MissingDataIsEmpty(string? data)
	{
		Assert.That(WidgetVariableReferenceParser.Parse(data).IsEmpty, Is.True);
	}

	[Test]
	public void SeveralReferencesAreAllFound()
	{
		var references = WidgetVariableReferenceParser.Parse("{\"label\":\"{{ vars.cpu }} / {{ vars.ram }}\"}");

		Assert.That(references.LabelNames, Is.EquivalentTo(["cpu", "ram"]));
	}

	/// <summary>
	/// A characterisation guard, deliberately: this passes both before and after ADR 0081, and its value is
	/// that it fails a well-meant future change that teaches the scanner about dots. An attribute path
	/// invalidates on the variable it hangs off - <c>vars.cpu.unit</c> is the unit of <c>cpu</c>, and there
	/// is no variable named <c>cpu.unit</c> for the index to key on, because a dot is not a legal character
	/// in a variable name. Yielding <c>cpu.unit</c> here would silently stop the label re-rendering when
	/// <c>cpu</c> changed.
	/// </summary>
	[Test]
	public void AnAttributePathInvalidatesOnTheVariableNotOnThePath()
	{
		var references = WidgetVariableReferenceParser.Parse("{\"label\":\"{{ vars.cpu }} {{ vars.cpu.unit }}\"}");

		Assert.Multiple(() =>
		{
			Assert.That(references.LabelNames, Is.EquivalentTo(["cpu"]));
			Assert.That(references.LabelNames, Does.Not.Contain("cpu.unit"));
			Assert.That(references.LabelNames, Does.Not.Contain("unit"));
		});
	}
}
