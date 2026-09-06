using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// The four state operators - isEmpty, isNotEmpty, isAvailable, isNotAvailable - ask about the left
/// operand's own state and ignore the right operand entirely. isEmpty/isNotEmpty are deliberately not
/// negations of each other: an operand that never resolved is neither, only unavailable. Only
/// isAvailable/isNotAvailable are true negations.
/// </summary>
[TestFixture]
public class ConditionStateOperatorTests
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

	private static readonly VariableEntity[] _catalog =
	[
		Var("artist_empty", VariableType.Text, string.Empty),
		Var("artist", VariableType.Text, "Radiohead"),
		Var("volume", VariableType.Numeric, "0"),
		Var("streaming", VariableType.Boolean, "false")
	];

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

	private static bool Evaluate(string leftJson, string op, string rightJson, params VariableEntity[] vars)
	{
		var (evaluator, context) = Build(vars);
		using var left = JsonDocument.Parse(leftJson);
		using var right = JsonDocument.Parse(rightJson);
		return evaluator.Evaluate(left.RootElement, op, right.RootElement, context);
	}

	// A1 - the twelve-cell truth table. U = an undeclared name, E = resolved to "", V = resolved to
	// "Radiohead".
	[TestCase("no_such_variable_at_all", "isEmpty", ExpectedResult = false)]
	[TestCase("no_such_variable_at_all", "isNotEmpty", ExpectedResult = false)]
	[TestCase("no_such_variable_at_all", "isAvailable", ExpectedResult = false)]
	[TestCase("no_such_variable_at_all", "isNotAvailable", ExpectedResult = true)]
	[TestCase("artist_empty", "isEmpty", ExpectedResult = true)]
	[TestCase("artist_empty", "isNotEmpty", ExpectedResult = false)]
	[TestCase("artist_empty", "isAvailable", ExpectedResult = true)]
	[TestCase("artist_empty", "isNotAvailable", ExpectedResult = false)]
	[TestCase("artist", "isEmpty", ExpectedResult = false)]
	[TestCase("artist", "isNotEmpty", ExpectedResult = true)]
	[TestCase("artist", "isAvailable", ExpectedResult = true)]
	[TestCase("artist", "isNotAvailable", ExpectedResult = false)]
	public bool The_twelve_cell_truth_table(string name, string op)
		=> Evaluate($$"""{"$var":"{{name}}"}""", op, "null", _catalog);

	// A2 - relationships, not literals. isEmpty and isNotEmpty are deliberately NOT complements: for an
	// operand that never resolved, both come out false at once, which no boolean negation can produce.
	[Test]
	public void An_unresolved_operand_is_neither_empty_nor_not_empty()
	{
		var isEmpty = Evaluate("""{"$var":"no_such_variable_at_all"}""", "isEmpty", "null", _catalog);
		var isNotEmpty = Evaluate("""{"$var":"no_such_variable_at_all"}""", "isNotEmpty", "null", _catalog);

		Assert.Multiple(() =>
		{
			Assert.That(isEmpty, Is.False);
			Assert.That(isNotEmpty, Is.False);
		});
	}

	[TestCase("no_such_variable_at_all")]
	[TestCase("artist_empty")]
	[TestCase("artist")]
	public void IsAvailable_and_isNotAvailable_are_always_true_negations(string name)
	{
		var available = Evaluate($$"""{"$var":"{{name}}"}""", "isAvailable", "null", _catalog);
		var notAvailable = Evaluate($$"""{"$var":"{{name}}"}""", "isNotAvailable", "null", _catalog);

		Assert.That(available, Is.Not.EqualTo(notAvailable));
	}

	[TestCase("artist_empty")]
	[TestCase("artist")]
	public void IsEmpty_and_isNotEmpty_are_true_negations_once_the_operand_has_resolved(string name)
	{
		var empty = Evaluate($$"""{"$var":"{{name}}"}""", "isEmpty", "null", _catalog);
		var notEmpty = Evaluate($$"""{"$var":"{{name}}"}""", "isNotEmpty", "null", _catalog);

		Assert.That(empty, Is.Not.EqualTo(notEmpty));
	}

	// A4 - the right operand cannot influence the verdict. Swept over eight shapes an author can leave
	// behind after switching operators, asserting both that the verdict is constant across the sweep and
	// that it equals the literal from the truth table above - constancy alone would be satisfied by an
	// operator that is constantly wrong.
	private static readonly (string Name, string Op, bool Expected)[] _stateOperatorCells =
	[
		("no_such_variable_at_all", "isEmpty", false),
		("no_such_variable_at_all", "isNotEmpty", false),
		("no_such_variable_at_all", "isAvailable", false),
		("no_such_variable_at_all", "isNotAvailable", true),
		("artist_empty", "isEmpty", true),
		("artist_empty", "isNotEmpty", false),
		("artist_empty", "isAvailable", true),
		("artist_empty", "isNotAvailable", false),
		("artist", "isEmpty", false),
		("artist", "isNotEmpty", true),
		("artist", "isAvailable", true),
		("artist", "isNotAvailable", false),

		// The load-bearing rows: a numeric variable whose value is the digit "0" must not fall through the
		// numeric fast path in EvaluateOperator, and an undeclared name's isNotAvailable must stay true even
		// when the right operand is the very string the name would otherwise compare equal to.
		("volume", "isEmpty", false),
		("volume", "isNotEmpty", true),
		("volume", "isAvailable", true),
		("volume", "isNotAvailable", false)
	];

	private static IEnumerable<(string Label, JsonElement Right)> RightShapes()
	{
		yield return ("json null", JsonDocument.Parse("null").RootElement.Clone());
		yield return ("right property absent", default);
		yield return ("empty string", JsonDocument.Parse("\"\"").RootElement.Clone());
		yield return ("string literal \"Radiohead\"", JsonDocument.Parse("\"Radiohead\"").RootElement.Clone());
		yield return ("numeric string \"0\"", JsonDocument.Parse("\"0\"").RootElement.Clone());
		yield return ("numeric 0", JsonDocument.Parse("0").RootElement.Clone());
		yield return ("numeric 128", JsonDocument.Parse("128").RootElement.Clone());
		yield return ("$var reference",
			JsonDocument.Parse("""{"$var":"no_such_variable_at_all"}""").RootElement.Clone());
	}

	private static IEnumerable<TestCaseData> _stateOperatorCellSource()
		=> _stateOperatorCells.Select(cell => new TestCaseData(cell.Name, cell.Op, cell.Expected)
			.SetArgDisplayNames(cell.Name, cell.Op));

	[TestCaseSource(nameof(_stateOperatorCellSource))]
	public void The_right_operand_cannot_influence_the_verdict(string name, string op, bool expected)
	{
		var (evaluator, context) = Build(_catalog);
		using var leftDoc = JsonDocument.Parse($$"""{"$var":"{{name}}"}""");

		foreach (var (label, right) in RightShapes())
		{
			var actual = evaluator.Evaluate(leftDoc.RootElement, op, right, context);
			Assert.That(actual, Is.EqualTo(expected), $"{name} {op} against right shape '{label}'");
		}
	}

	// A5 - consequences of the single "available && string form is empty" rule.
	[Test]
	public void A_numeric_zero_is_available_and_not_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Evaluate("""{"$var":"volume"}""", "isAvailable", "null", _catalog), Is.True);
			Assert.That(Evaluate("""{"$var":"volume"}""", "isEmpty", "null", _catalog), Is.False);
			Assert.That(Evaluate("""{"$var":"volume"}""", "isNotEmpty", "null", _catalog), Is.True);
		});
	}

	[Test]
	public void A_false_boolean_is_available_and_not_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Evaluate("""{"$var":"streaming"}""", "isAvailable", "null", _catalog), Is.True);
			Assert.That(Evaluate("""{"$var":"streaming"}""", "isEmpty", "null", _catalog), Is.False);
			Assert.That(Evaluate("""{"$var":"streaming"}""", "isNotEmpty", "null", _catalog), Is.True);
		});
	}

	[Test]
	public void An_empty_string_literal_operand_is_available_and_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Evaluate("\"\"", "isAvailable", "null", _catalog), Is.True);
			Assert.That(Evaluate("\"\"", "isEmpty", "null", _catalog), Is.True);
			Assert.That(Evaluate("\"\"", "isNotEmpty", "null", _catalog), Is.False);
		});
	}

	[Test]
	public void A_non_empty_string_literal_operand_is_available_and_not_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Evaluate("\"Radiohead\"", "isAvailable", "null", _catalog), Is.True);
			Assert.That(Evaluate("\"Radiohead\"", "isEmpty", "null", _catalog), Is.False);
		});
	}

	[Test]
	public void A_literal_json_null_operand_is_not_available()
		=> Assert.That(Evaluate("null", "isNotAvailable", "null", _catalog), Is.True);

	// Why the UI forbids authoring a state operator against free text. A free-text operand is rendered as a
	// Liquid template before it is compared (ResolveSide's String case), so it always resolves to a string
	// no matter what it references - and the two spellings of the same undeclared name then give opposite
	// answers. The reference is the trustworthy one; the free text reports a variable that does not exist
	// as an available, empty value. (A variable that is declared but currently unavailable renders as
	// UnavailablePlaceholder instead, so free text reports it as available and NOT empty - the same lie
	// with the opposite emptiness; the staleness machinery for that lives in
	// VariableCatalogConditionParityTests.)
	[Test]
	public void A_free_text_operand_contradicts_the_reference_spelling_of_the_same_name()
	{
		const string name = "no_such_variable_at_all";

		Assert.Multiple(() =>
		{
			Assert.That(Evaluate($"{{\"$var\":\"{name}\"}}", "isNotAvailable", "null", _catalog), Is.True);
			Assert.That(Evaluate($"{{\"$var\":\"{name}\"}}", "isEmpty", "null", _catalog), Is.False);

			Assert.That(Evaluate($"\"{{{{ vars.{name} }}}}\"", "isNotAvailable", "null", _catalog), Is.False);
			Assert.That(Evaluate($"\"{{{{ vars.{name} }}}}\"", "isAvailable", "null", _catalog), Is.True);
			Assert.That(Evaluate($"\"{{{{ vars.{name} }}}}\"", "isEmpty", "null", _catalog), Is.True);
		});
	}
}
