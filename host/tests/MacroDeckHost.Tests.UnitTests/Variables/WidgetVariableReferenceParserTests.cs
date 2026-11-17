using System.Text.Json.Nodes;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class WidgetVariableReferenceParserTests
{
	[Test]
	public void A_label_that_only_reads_the_time_is_recorded_as_reading_the_clock()
	{
		var references = WidgetVariableReferenceParser.Parse("{\"label\":\"{{ 'now' | date: '%H:%M' }}\"}");

		Assert.Multiple(() =>
		{
			Assert.That(references.IsEmpty, Is.False);
			Assert.That(references.LabelNames, Is.EquivalentTo([TemplateClock.Name]));
			Assert.That(references.StateMappingNames, Is.Empty);
		});
	}

	[TestCase("{\"label\":\"{{ date.now | date.to_string '%H' }}\"}")]
	[TestCase("{\"label\":\"{{ date.utc_now.year }}\"}")]
	[TestCase("{\"label\":\"{{ \\\"today\\\" | date }}\"}")]
	public void Every_way_of_reading_the_time_is_recognised(string data)
	{
		Assert.That(WidgetVariableReferenceParser.Parse(data).LabelNames, Does.Contain(TemplateClock.Name));
	}

	[Test]
	public void A_time_label_is_still_recognised_after_the_host_rewrites_the_widget_data()
	{
		var stored = JsonNode.Parse("{\"label\":\"{{ 'now' | date: '%H:%M' }} {{ \\\"today\\\" | date }}\"}")!
			.ToJsonString();

		var references = WidgetVariableReferenceParser.Parse(stored);

		Assert.Multiple(() =>
		{
			Assert.That(stored, Does.Contain("\\u0027now\\u0027"));
			Assert.That(references.LabelNames, Does.Contain(TemplateClock.Name));
		});
	}

	[Test]
	public void A_state_mapping_reading_the_time_is_recorded_as_reading_the_clock()
	{
		var data = "{\"label\":\"{{ vars.cpu }}\","
			+ "\"stateMapping\":{\"rules\":[{\"when\":\"{{ date.now.hour }}\"}]}}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.That(references.StateMappingNames, Is.SupersetOf(new[] { "cpu", TemplateClock.Name }));
	}

	[Test]
	public void A_label_that_does_not_read_the_time_is_not_recorded_as_reading_the_clock()
	{
		var data = "{\"label\":\"{{ '2016-01-05' | date: '%Y' }} {{ vars.cpu }}\"}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.That(references.LabelNames, Is.EquivalentTo(["cpu"]));
	}

	[Test]
	public void Referenced_names_of_a_widget_text_include_the_clock_when_it_reads_the_time()
	{
		Assert.Multiple(() =>
		{
			Assert.That(WidgetVariableReferenceParser.ReferencedNames("{{ date.now.hour }} {{ vars.cpu }}"),
				Is.EquivalentTo(new[] { "cpu", TemplateClock.Name }));
			Assert.That(WidgetVariableReferenceParser.ReferencedNames("{{ vars.cpu }}"), Is.EquivalentTo(["cpu"]));
		});
	}

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
		var data = "{\"mode\":\"toggle\",\"stateBinding\":{\"kind\":\"compare\","
			+ "\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}}";

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
		var data = "{\"stateBinding\":{\"kind\":\"compare\","
			+ "\"left\":\"{{ vars.mode }}\",\"operator\":\"==\",\"right\":\"on\"}}";

		var references = WidgetVariableReferenceParser.Parse(data);

		Assert.That(references.StateMappingNames, Does.Contain("mode"));
	}

	[Test]
	public void EscapedNestedFlowJson_isFound()
	{
		var data
			= "{\"flows\":\"[{\\\"blocks\\\":[{\\\"parameters\\\":{\\\"value\\\":{\\\"$var\\\":\\\"cpu\\\"}}}]}]\","
			+ "\"stateBinding\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"}}}";

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
		var data = "{\"stateBinding\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"}},"
			+ "\"flows\":\"[{\\\"action\\\":\\\"set-variable\\\",\\\"target\\\":\\\"counter\\\"}]\"}";

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
