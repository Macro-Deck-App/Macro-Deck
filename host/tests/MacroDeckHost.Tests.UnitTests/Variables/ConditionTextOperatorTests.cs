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
		Var("track", VariableType.Text, "007")
	];

	private static bool Evaluate(string leftJson, string op, string rightJson)
	{
		var registry = new VariableRegistry();
		foreach (var variable in _catalog)
		{
			registry.Upsert(variable);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
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
}
