using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ActionConditionEvaluatorExpressionTests
{
	private static VariableEntity Var(string name, VariableType type, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		Value = value
	};

	private static (ActionConditionEvaluator Evaluator, VariableContext Context) Build(params VariableEntity[] vars)
	{
		var registry = new VariableRegistry();
		foreach (var v in vars)
		{
			registry.Upsert(v);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
		return (new ActionConditionEvaluator(renderer), context);
	}

	private static bool Evaluate(string expressionJson, params VariableEntity[] vars)
	{
		var (evaluator, context) = Build(vars);
		using var document = JsonDocument.Parse(expressionJson);
		return evaluator.EvaluateExpression(document.RootElement, context);
	}

	[Test]
	public void SingleComparison_true_when_bound_boolean_matches()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":true}",
			Var("streaming", VariableType.Boolean, "true"));

		Assert.That(result, Is.True);
	}

	[Test]
	public void SingleComparison_false_when_bound_boolean_differs()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":true}",
			Var("streaming", VariableType.Boolean, "false"));

		Assert.That(result, Is.False);
	}

	[Test]
	public void SingleComparison_true_when_boolean_matches_string_literal_true()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":\"true\"}",
			Var("streaming", VariableType.Boolean, "true"));

		Assert.That(result, Is.True);
	}

	[Test]
	public void SingleComparison_true_when_boolean_false_matches_string_literal_false()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":\"false\"}",
			Var("streaming", VariableType.Boolean, "false"));

		Assert.That(result, Is.True);
	}

	[Test]
	public void SingleComparison_false_when_boolean_differs_from_string_literal()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":\"true\"}",
			Var("streaming", VariableType.Boolean, "false"));

		Assert.That(result, Is.False);
	}

	[Test]
	public void SingleComparison_notEquals_true_when_boolean_differs_from_string_literal()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"!=\",\"right\":\"true\"}",
			Var("streaming", VariableType.Boolean, "false"));

		Assert.That(result, Is.True);
	}

	[Test]
	public void NumericComparison_uses_numeric_semantics()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}",
			Var("cpu", VariableType.Numeric, "85"));

		Assert.That(result, Is.True);
	}

	[Test]
	public void And_is_true_only_when_all_leaves_true()
	{
		const string expr =
			"{\"kind\":\"and\",\"id\":\"g\",\"operands\":[" +
			"{\"kind\":\"compare\",\"id\":\"a\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":true}," +
			"{\"kind\":\"compare\",\"id\":\"b\",\"left\":{\"$var\":\"cpu\"},\"operator\":\"<\",\"right\":90}]}";

		Assert.Multiple(() =>
		{
			Assert.That(Evaluate(expr,
					Var("streaming", VariableType.Boolean, "true"),
					Var("cpu", VariableType.Numeric, "85")),
				Is.True);
			Assert.That(Evaluate(expr,
					Var("streaming", VariableType.Boolean, "true"),
					Var("cpu", VariableType.Numeric, "95")),
				Is.False);
		});
	}

	[Test]
	public void Or_is_true_when_any_leaf_true()
	{
		const string expr =
			"{\"kind\":\"or\",\"id\":\"g\",\"operands\":[" +
			"{\"kind\":\"compare\",\"id\":\"a\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":true}," +
			"{\"kind\":\"compare\",\"id\":\"b\",\"left\":{\"$var\":\"recording\"},\"operator\":\"==\",\"right\":true}]}";

		Assert.That(Evaluate(expr,
				Var("streaming", VariableType.Boolean, "false"),
				Var("recording", VariableType.Boolean, "true")),
			Is.True);
	}

	[Test]
	public void UnknownVariable_resolves_to_false()
	{
		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"missing\"},\"operator\":\"==\",\"right\":true}");

		Assert.That(result, Is.False);
	}

	/// <summary>
	/// ADR 0081 puts attribute paths inside template expressions only, and says outright that
	/// <c>ActionConditionEvaluator</c> was not extended to reach them: it resolves a name against the
	/// registry on its own path, and a dot is not a legal variable-name character. So <c>$var</c> naming an
	/// attribute path is an unknown variable, which the evaluator already answers safely. A condition that
	/// started resolving <c>x.unit</c> would be comparing against a string the automation author never
	/// stored, which is worse than not resolving it at all.
	/// </summary>
	[Test]
	public void An_attribute_path_in_a_typed_reference_does_not_resolve()
	{
		var cpu = Var("cpu", VariableType.Numeric, "85");
		cpu.Unit = "%";

		var result = Evaluate(
			"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"cpu.unit\"},\"operator\":\"==\",\"right\":\"%\"}",
			cpu);

		Assert.That(result, Is.False);
	}

	[Test]
	public void Leaves_are_reported_and_short_circuited_operands_get_null_result()
	{
		const string expr =
			"{\"kind\":\"and\",\"id\":\"g\",\"operands\":[" +
			"{\"kind\":\"compare\",\"id\":\"a\",\"left\":{\"$var\":\"streaming\"},\"operator\":\"==\",\"right\":true}," +
			"{\"kind\":\"compare\",\"id\":\"b\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}]}";

		var (evaluator, context) = Build(Var("streaming", VariableType.Boolean, "false"),
			Var("cpu", VariableType.Numeric, "85"));
		var leaves = new Dictionary<string, ConditionLeafOutcome>();
		using var document = JsonDocument.Parse(expr);

		var result = evaluator.EvaluateExpression(document.RootElement, context, leaf => leaves[leaf.Id] = leaf);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.False);
			Assert.That(leaves["a"].Result, Is.False);
			Assert.That(leaves["b"].Result, Is.Null);
		});
	}

	// A7 - a state operator leaf inside a nested expression: no right side is resolved for it (RightDisplay
	// stays empty), and its verdict still participates in short-circuiting the sibling leaf normally.
	private const string StateAndEqualityExpression =
		"{\"kind\":\"and\",\"id\":\"root\",\"operands\":[" +
		"{\"kind\":\"compare\",\"id\":\"c1\",\"left\":{\"$var\":\"artist\"},\"operator\":\"isNotEmpty\",\"right\":\"\"}," +
		"{\"kind\":\"compare\",\"id\":\"c2\",\"left\":{\"$var\":\"track\"},\"operator\":\"==\",\"right\":\"Creep\"}]}";

	[Test]
	public void A_state_operator_leaf_reports_no_right_display_and_the_aggregate_is_true()
	{
		var (evaluator, context) = Build(Var("artist", VariableType.Text, "Radiohead"),
			Var("track", VariableType.Text, "Creep"));
		var leaves = new Dictionary<string, ConditionLeafOutcome>();
		using var document = JsonDocument.Parse(StateAndEqualityExpression);

		var result = evaluator.EvaluateExpression(document.RootElement, context, leaf => leaves[leaf.Id] = leaf);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.True);
			Assert.That(leaves["c1"].Result, Is.True);
			Assert.That(leaves["c1"].Error, Is.Null);
			Assert.That(leaves["c1"].LeftDisplay, Is.EqualTo("Radiohead"));
			Assert.That(leaves["c1"].RightDisplay, Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public void An_unresolved_left_operand_on_a_state_leaf_short_circuits_the_next_leaf()
	{
		var (evaluator, context) = Build(Var("track", VariableType.Text, "Creep"));
		var leaves = new Dictionary<string, ConditionLeafOutcome>();
		using var document = JsonDocument.Parse(StateAndEqualityExpression);

		var result = evaluator.EvaluateExpression(document.RootElement, context, leaf => leaves[leaf.Id] = leaf);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.False);
			Assert.That(leaves["c1"].Result, Is.False);
			Assert.That(leaves["c1"].LeftDisplay, Is.EqualTo(string.Empty));
			Assert.That(leaves["c1"].Error, Is.Null);
			Assert.That(leaves["c2"].Result, Is.Null);
		});
	}
}
