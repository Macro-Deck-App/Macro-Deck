using System.Text.Json;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ScriptInputContextTests
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

	private static ScriptInput Declare(
		string name,
		ScriptInputType type = ScriptInputType.Text,
		string? defaultValue = null) => new()
	{
		Name = name,
		Type = type,
		DefaultValue = defaultValue
	};

	private static (ActionConditionEvaluator Evaluator, VariableContext Context) Build(
		IReadOnlyList<ScriptInput> declarations,
		IReadOnlyDictionary<string, object?>? supplied,
		params VariableEntity[] vars)
	{
		var registry = new VariableRegistry();
		foreach (var v in vars)
		{
			registry.Upsert(v);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		var binding = ScriptInputBinder.Bind(declarations, supplied);
		Assert.That(binding.Success, Is.True, binding.ErrorMessage);

		return (new ActionConditionEvaluator(renderer), context.WithInputs(binding.Values));
	}

	private static object? Reference(string name, VariableContext context)
	{
		using var document = JsonDocument.Parse($$"""{"$var":"{{name}}"}""");
		return ActionConditionEvaluator.ResolveVariableReference(document.RootElement, context);
	}

	private static Dictionary<string, object?> Supplied(params (string Name, object? Value)[] values)
		=> values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

	[Test]
	public void Input_resolves_in_a_liquid_template()
	{
		var (evaluator, context) = Build([Declare("scene")], Supplied(("scene", "Live")));

		Assert.That(evaluator.RenderTemplateString("Now: {{ vars.scene }}", context), Is.EqualTo("Now: Live"));
	}

	[Test]
	public void Input_reference_keeps_the_declared_type()
	{
		var (_, context) = Build([
				Declare("scene"),
				Declare("volume", ScriptInputType.Numeric),
				Declare("muted", ScriptInputType.Boolean)
			],
			Supplied(("scene", "Live"), ("volume", 42), ("muted", true)));

		Assert.Multiple(() =>
		{
			Assert.That(Reference("scene", context), Is.EqualTo("Live"));
			Assert.That(Reference("volume", context), Is.EqualTo(42d));
			Assert.That(Reference("muted", context), Is.EqualTo(true));
		});
	}

	[Test]
	public void Input_shadows_a_global_of_the_same_name_in_both_forms()
	{
		var (evaluator, context) = Build([Declare("scene")],
			Supplied(("scene", "Live")),
			Var("scene", VariableType.Text, "Starting Soon"));

		Assert.Multiple(() =>
		{
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", context), Is.EqualTo("Live"));
			Assert.That(Reference("scene", context), Is.EqualTo("Live"));
		});
	}

	[Test]
	public void Unshadowed_globals_still_resolve_alongside_the_overlay()
	{
		var (evaluator, context) = Build([Declare("scene")],
			Supplied(("scene", "Live")),
			Var("streaming", VariableType.Boolean, "true"));

		Assert.Multiple(() =>
		{
			Assert.That(Reference("streaming", context), Is.EqualTo(true));
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", context), Is.EqualTo("Live"));
		});
	}

	[Test]
	public void A_supplied_name_the_script_never_declared_is_not_readable()
	{
		var (evaluator, context) = Build([Declare("scene")],
			Supplied(("scene", "Live"), ("secret", "hunter2")));

		Assert.Multiple(() =>
		{
			Assert.That(evaluator.RenderTemplateString("{{ vars.secret }}", context), Is.Empty);
			Assert.That(Reference("secret", context), Is.Null);
		});
	}

	[Test]
	public void A_supplied_name_the_script_never_declared_does_not_shadow_a_global_either()
	{
		var (_, context) = Build([Declare("scene")],
			Supplied(("scene", "Live"), ("secret", "hunter2")),
			Var("secret", VariableType.Text, "global"));

		Assert.That(Reference("secret", context), Is.EqualTo("global"));
	}

	[Test]
	public void An_omitted_input_falls_back_to_its_declared_default()
	{
		var (evaluator, context) = Build([Declare("scene", defaultValue: "Starting Soon")], Supplied());

		Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", context), Is.EqualTo("Starting Soon"));
	}

	[Test]
	public void A_supplied_value_beats_the_default_even_when_it_is_falsy()
	{
		var (_, context) = Build([
				Declare("scene", defaultValue: "Starting Soon"),
				Declare("count", ScriptInputType.Numeric, "5"),
				Declare("muted", ScriptInputType.Boolean, "true")
			],
			Supplied(("scene", ""), ("count", 0), ("muted", false)));

		Assert.Multiple(() =>
		{
			Assert.That(Reference("scene", context), Is.EqualTo(string.Empty));
			Assert.That(Reference("count", context), Is.EqualTo(0d));
			Assert.That(Reference("muted", context), Is.EqualTo(false));
		});
	}

	// The point of matching the variable types: a global of a type can be handed to an input of the
	// same type and still read back as that type.
	[Test]
	public void A_global_can_be_handed_through_to_an_input_of_the_same_type()
	{
		var (_, context) = Build([
				Declare("volume", ScriptInputType.Numeric),
				Declare("muted", ScriptInputType.Boolean)
			],
			Supplied(("volume", "70"), ("muted", "true")),
			Var("volume", VariableType.Numeric, "70"),
			Var("muted", VariableType.Boolean, "true"));

		Assert.Multiple(() =>
		{
			Assert.That(Reference("volume", context), Is.EqualTo(70d));
			Assert.That(Reference("muted", context), Is.EqualTo(true));
		});
	}

	[Test]
	public void Inputs_and_event_parameters_compose_in_either_order()
	{
		var registry = new VariableRegistry();
		registry.Upsert(Var("scene", VariableType.Text, "Starting Soon"));
		var renderer = new VariableTemplateRenderer(registry);
		var evaluator = new ActionConditionEvaluator(renderer);
		var baseContext = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		var inputs = new Dictionary<string, object?>(StringComparer.Ordinal) { ["scene"] = "Live" };
		var occurrence = new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Break" };

		var inputsFirst = baseContext.WithInputs(inputs).WithEvent(occurrence);
		var eventFirst = baseContext.WithEvent(occurrence).WithInputs(inputs);

		Assert.Multiple(() =>
		{
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}/{{ event.sceneName }}", inputsFirst),
				Is.EqualTo("Live/Break"));
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}/{{ event.sceneName }}", eventFirst),
				Is.EqualTo("Live/Break"));
			Assert.That(Reference("scene", inputsFirst), Is.EqualTo("Live"));
			Assert.That(Reference("scene", eventFirst), Is.EqualTo("Live"));
		});
	}

	[Test]
	public void WithInputs_does_not_mutate_the_context_it_was_derived_from()
	{
		var registry = new VariableRegistry();
		registry.Upsert(Var("scene", VariableType.Text, "Starting Soon"));
		var renderer = new VariableTemplateRenderer(registry);
		var evaluator = new ActionConditionEvaluator(renderer);
		var baseContext = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		var first = baseContext.WithInputs(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["scene"] = "One" });
		var second = baseContext.WithInputs(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["scene"] = "Two" });

		Assert.Multiple(() =>
		{
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", first), Is.EqualTo("One"));
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", second), Is.EqualTo("Two"));
			Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", baseContext), Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public void An_overlay_does_not_leak_into_the_shared_empty_context()
	{
		var evaluator = new ActionConditionEvaluator(new VariableTemplateRenderer(new VariableRegistry()));

		VariableContext.Empty.WithInputs(new Dictionary<string, object?>(StringComparer.Ordinal)
			{ ["scene"] = "Live" });

		Assert.That(evaluator.RenderTemplateString("{{ vars.scene }}", VariableContext.Empty), Is.Empty);
	}
}
