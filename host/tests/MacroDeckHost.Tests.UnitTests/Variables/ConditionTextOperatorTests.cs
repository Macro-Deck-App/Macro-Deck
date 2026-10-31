using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ConditionTextOperatorTests
{
	private static VariableEntity Var(string name, VariableType type, string value)
	{
		return new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = VariableScope.Global,
			Type = type,
			Classification = VariableClassification.User,
			Value = value
		};
	}

	private static readonly VariableEntity[] _catalog =
	[
		Var("code", VariableType.Text, "12345"),
		Var("code_number", VariableType.Numeric, "12345"),
		Var("track", VariableType.Text, "007"),
		Var("rate", VariableType.Numeric, "1.5"),
		Var("price", VariableType.Numeric, "12.5"),
		Var("scaled", VariableType.Numeric, "1.50"),
		Var("zero_padded", VariableType.Numeric, "007"),
		Var("big", VariableType.Numeric, "123456789012345678")
	];

	private static (VariableTemplateRenderer Renderer, VariableContext Context) Catalog()
	{
		var registry = new VariableRegistry();
		foreach (var variable in _catalog)
		{
			registry.Upsert(variable);
		}

		var renderer = new VariableTemplateRenderer(registry);
		return (renderer, renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult());
	}

	private static bool Evaluate(string leftJson, string op, string rightJson)
	{
		var (renderer, context) = Catalog();
		using var left = JsonDocument.Parse(leftJson);
		using var right = JsonDocument.Parse(rightJson);
		return new ActionConditionEvaluator(renderer).Evaluate(left.RootElement, op, right.RootElement, context);
	}

	[TestCase("\"{{ vars.code }}\"", "contains", "\"23\"", ExpectedResult = true)]
	[TestCase("\"{{ vars.code }}\"", "contains", "23", ExpectedResult = true)]
	[TestCase("\"{{ vars.code }}\"", "contains", "\"99\"", ExpectedResult = false)]
	[TestCase("\"{{ vars.code }}\"", "startsWith", "\"12\"", ExpectedResult = true)]
	[TestCase("\"{{ vars.code }}\"", "startsWith", "\"23\"", ExpectedResult = false)]
	[TestCase("\"{{ vars.code }}\"", "contains", "\"12345\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"code_number\"}", "contains", "\"23\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"code_number\"}", "startsWith", "1", ExpectedResult = true)]
	[TestCase("\"{{ vars.track }}\"", "startsWith", "\"0\"", ExpectedResult = true)]
	[TestCase("\"{{ vars.track }}\"", "contains", "\"7\"", ExpectedResult = true)]
	[TestCase("\"1.50\"", "contains", "\"1.5\"", ExpectedResult = true)]
	[TestCase("\"1.50\"", "startsWith", "\"1.500\"", ExpectedResult = false)]
	public bool Text_operators_compare_number_like_operands_as_text(string left, string op, string right)
	{
		return Evaluate(left, op, right);
	}

	[TestCase("\"10\"", ">", "\"9\"", ExpectedResult = true)]
	[TestCase("\"1.0\"", "==", "1", ExpectedResult = true)]
	[TestCase("{\"$var\":\"code_number\"}", "==", "\"12345\"", ExpectedResult = true)]
	public bool Comparison_operators_still_compare_number_like_operands_as_numbers(
		string left,
		string op,
		string right)
	{
		return Evaluate(left, op, right);
	}

	[SetCulture("de-DE")]
	[TestCase("{\"$var\":\"rate\"}", "contains", "\"1.5\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"rate\"}", "contains", "\"1,5\"", ExpectedResult = false)]
	[TestCase("{\"$var\":\"price\"}", "==", "\"12,5\"", ExpectedResult = false)]
	[TestCase("{\"$var\":\"price\"}", "!=", "\"12,5\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"big\"}", "contains", "\"789012345678\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"scaled\"}", "contains", "\"1.50\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"zero_padded\"}", "startsWith", "\"00\"", ExpectedResult = true)]
	[TestCase("\"a1.5\"", "contains", "1.5", ExpectedResult = true)]
	public bool Text_comparison_reads_a_number_the_same_on_a_comma_culture_host(
		string left,
		string op,
		string right)
	{
		return Evaluate(left, op, right);
	}

	[SetCulture("de-DE")]
	[TestCase("{\"$var\":\"zero_padded\"}", "==", "7", ExpectedResult = true)]
	[TestCase("{\"$var\":\"scaled\"}", "==", "\"1.5\"", ExpectedResult = true)]
	[TestCase("{\"$var\":\"price\"}", ">", "9", ExpectedResult = true)]
	[TestCase("{\"$var\":\"price\"}", "==", "12.5", ExpectedResult = true)]
	public bool Numeric_operators_compare_a_numeric_variable_by_value(string left, string op, string right)
	{
		return Evaluate(left, op, right);
	}

	[Test]
	[SetCulture("de-DE")]
	public void An_event_number_is_compared_as_invariant_text()
	{
		var (renderer, context) = Catalog();
		var withEvent = context.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 1.5d });
		using var left = JsonDocument.Parse("{\"$event\":\"value\"}");
		using var right = JsonDocument.Parse("\"1.5\"");

		Assert.That(new ActionConditionEvaluator(renderer).Evaluate(left.RootElement,
				"contains",
				right.RootElement,
				withEvent),
			Is.True);
	}

	[Test]
	[SetCulture("de-DE")]
	public void A_condition_badge_shows_the_stored_text_it_compared()
	{
		var (renderer, context) = Catalog();
		using var expression = JsonDocument.Parse("""
												  { "kind": "compare", "id": "c", "left": { "$var": "scaled" }, "operator": "contains", "right": 1.5 }
												  """);
		ConditionLeafOutcome? outcome = null;

		new ActionConditionEvaluator(renderer).EvaluateExpression(expression.RootElement,
			context,
			leaf => outcome = leaf);

		Assert.That(outcome?.LeftDisplay, Is.EqualTo("1.50"));
		Assert.That(outcome?.RightDisplay, Is.EqualTo("1.5"));
	}

	[Test]
	public void Action_parameters_still_read_a_numeric_variable_as_a_number()
	{
		var (_, context) = Catalog();
		using var reference = JsonDocument.Parse("{\"$var\":\"scaled\"}");

		Assert.That(ActionConditionEvaluator.ResolveVariableReference(reference.RootElement, context),
			Is.EqualTo(1.5d));
	}
}
