using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class EventParameterContextTests
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

	private static (ActionConditionEvaluator Evaluator, VariableContext Context) Build(
		IReadOnlyDictionary<string, object?>? eventParameters,
		params VariableEntity[] vars)
	{
		var registry = new VariableRegistry();
		foreach (var v in vars)
		{
			registry.Upsert(v);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
		if (eventParameters is not null)
		{
			context = context.WithEvent(eventParameters);
		}

		return (new ActionConditionEvaluator(renderer), context);
	}

	private static object? Resolve(string referenceJson, IReadOnlyDictionary<string, object?>? eventParameters)
	{
		var (_, context) = Build(eventParameters);
		using var document = JsonDocument.Parse(referenceJson);
		return ActionConditionEvaluator.ResolveVariableReference(document.RootElement, context);
	}

	[Test]
	public void Event_reference_resolves_the_occurrence_parameter()
	{
		var value = Resolve("""{"$event":"sceneName"}""",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Starting Soon" });

		Assert.That(value, Is.EqualTo("Starting Soon"));
	}

	[Test]
	public void Event_reference_keeps_the_parameter_type()
	{
		var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["volume"] = 42d,
			["muted"] = true
		};

		Assert.Multiple(() =>
		{
			Assert.That(Resolve("""{"$event":"volume"}""", parameters), Is.EqualTo(42d));
			Assert.That(Resolve("""{"$event":"muted"}""", parameters), Is.EqualTo(true));
		});
	}

	[Test]
	public void Unknown_event_parameter_resolves_to_null_rather_than_throwing()
	{
		var value = Resolve("""{"$event":"nope"}""",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Live" });

		Assert.That(value, Is.Null);
	}

	[Test]
	public void Event_reference_on_a_run_without_an_occurrence_resolves_to_null()
	{
		Assert.That(Resolve("""{"$event":"sceneName"}""", eventParameters: null), Is.Null);
	}

	[Test]
	public void Variable_reference_still_resolves_alongside_the_event_overlay()
	{
		var (_, context) = Build(new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Live" },
			Var("streaming", VariableType.Boolean, "true"));

		using var document = JsonDocument.Parse("""{"$var":"streaming"}""");

		Assert.That(ActionConditionEvaluator.ResolveVariableReference(document.RootElement, context), Is.EqualTo(true));
	}

	[Test]
	public void Event_parameter_is_comparable_in_a_condition_expression()
	{
		var (evaluator, context) = Build(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["sceneName"] = "Live" });

		using var document = JsonDocument.Parse(
			"""{"kind":"compare","id":"c1","left":{"$event":"sceneName"},"operator":"==","right":"Live"}""");

		Assert.That(evaluator.EvaluateExpression(document.RootElement, context), Is.True);
	}

	[Test]
	public void Event_parameter_renders_in_a_liquid_template()
	{
		var (evaluator, context) = Build(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["sceneName"] = "Starting Soon" });

		Assert.That(evaluator.RenderTemplateString("Now: {{ event.sceneName }}", context),
			Is.EqualTo("Now: Starting Soon"));
	}

	[Test]
	public void Event_template_on_a_run_without_an_occurrence_renders_empty()
	{
		var (evaluator, context) = Build(eventParameters: null);

		Assert.That(evaluator.RenderTemplateString("Now: {{ event.sceneName }}", context), Is.EqualTo("Now: "));
	}

	[Test]
	public void Empty_context_carries_the_event_root()
	{
		var registry = new VariableRegistry();
		var evaluator = new ActionConditionEvaluator(new VariableTemplateRenderer(registry));

		Assert.That(evaluator.RenderTemplateString("{{ event.anything }}", VariableContext.Empty), Is.Empty);
	}

	[Test]
	public void WithEvent_does_not_mutate_the_source_context()
	{
		var (evaluator, baseContext) = Build(eventParameters: null);

		var first = baseContext.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["sceneName"] = "One" });
		var second = baseContext.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["sceneName"] = "Two" });

		Assert.Multiple(() =>
		{
			Assert.That(evaluator.RenderTemplateString("{{ event.sceneName }}", first), Is.EqualTo("One"));
			Assert.That(evaluator.RenderTemplateString("{{ event.sceneName }}", second), Is.EqualTo("Two"));
			Assert.That(evaluator.RenderTemplateString("{{ event.sceneName }}", baseContext), Is.Empty);
		});
	}

	[Test]
	public void WithEvent_keeps_the_variable_snapshot()
	{
		var (evaluator, baseContext) = Build(eventParameters: null, Var("scene", VariableType.Text, "Live"));

		var withEvent = baseContext.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["sceneName"] = "Break" });

		Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}/{{ event.sceneName }}", withEvent),
			Is.EqualTo("Live/Break"));
	}

	// A6 - the four state operators against an $event operand. "producer" is present in the payload but
	// holds a null value; "nothing_declared" is absent from the payload entirely. Both must resolve
	// identically - a key that is present but null counts as not resolved, exactly like a key that was
	// never delivered.
	[TestCase("artist", "isAvailable", ExpectedResult = true)]
	[TestCase("artist", "isEmpty", ExpectedResult = false)]
	[TestCase("artist", "isNotEmpty", ExpectedResult = true)]
	[TestCase("artist", "isNotAvailable", ExpectedResult = false)]
	[TestCase("album", "isAvailable", ExpectedResult = true)]
	[TestCase("album", "isEmpty", ExpectedResult = true)]
	[TestCase("album", "isNotEmpty", ExpectedResult = false)]
	[TestCase("album", "isNotAvailable", ExpectedResult = false)]
	[TestCase("producer", "isAvailable", ExpectedResult = false)]
	[TestCase("producer", "isNotAvailable", ExpectedResult = true)]
	[TestCase("producer", "isEmpty", ExpectedResult = false)]
	[TestCase("producer", "isNotEmpty", ExpectedResult = false)]
	[TestCase("nothing_declared", "isAvailable", ExpectedResult = false)]
	[TestCase("nothing_declared", "isNotAvailable", ExpectedResult = true)]
	[TestCase("nothing_declared", "isEmpty", ExpectedResult = false)]
	[TestCase("nothing_declared", "isNotEmpty", ExpectedResult = false)]
	public bool Event_parameter_state_operators_answer_per_key(string name, string op)
	{
		var (evaluator, context) = Build(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["artist"] = "Radiohead",
			["album"] = "",
			["producer"] = null
		});

		using var left = JsonDocument.Parse($$"""{"$event":"{{name}}"}""");
		using var right = JsonDocument.Parse("null");
		return evaluator.Evaluate(left.RootElement, op, right.RootElement, context);
	}

	[Test]
	public void A_present_null_parameter_and_an_absent_one_are_reported_identically()
	{
		var (evaluator, context) = Build(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["producer"] = null
		});

		bool EvaluateState(string name, string op)
		{
			using var left = JsonDocument.Parse($$"""{"$event":"{{name}}"}""");
			using var right = JsonDocument.Parse("null");
			return evaluator.Evaluate(left.RootElement, op, right.RootElement, context);
		}

		Assert.Multiple(() =>
		{
			Assert.That(EvaluateState("producer", "isNotAvailable"),
				Is.EqualTo(EvaluateState("nothing_declared", "isNotAvailable")));
			Assert.That(EvaluateState("producer", "isAvailable"),
				Is.EqualTo(EvaluateState("nothing_declared", "isAvailable")));
			Assert.That(EvaluateState("producer", "isEmpty"), Is.EqualTo(EvaluateState("nothing_declared", "isEmpty")));
			Assert.That(EvaluateState("producer", "isNotEmpty"),
				Is.EqualTo(EvaluateState("nothing_declared", "isNotEmpty")));
		});
	}
}
