using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// ADR 0081's central promise: <c>vars.x</c> keeps being the scalar it has always been while
/// <c>vars.x.unit</c> resolves on the same reference. Almost every case here renders one template twice -
/// once against an attributed variable, once against the same variable with no attributes - and asserts
/// both produce the same literal, because "the container is transparent" is a claim about the difference
/// between those two runs and not about either one alone.
/// </summary>
[TestFixture]
public class VariableAttributeTemplateTests
{
	private static string Bare(string template, params VariableEntity[] vars) => Render(template, vars);

	private static string Attributed(string template, params VariableEntity[] vars)
	{
		foreach (var variable in vars)
		{
			variable.Unit = "%";
		}

		return Render(template, vars);
	}

	private static void Both(string template, string expected, params Func<VariableEntity>[] vars)
	{
		Assert.Multiple(() =>
		{
			Assert.That(Bare(template, [.. vars.Select(factory => factory())]), Is.EqualTo(expected), "bare");
			Assert.That(Attributed(template, [.. vars.Select(factory => factory())]),
				Is.EqualTo(expected),
				"attributed");
		});
	}

	private static string Render(string template, IEnumerable<VariableEntity> vars)
	{
		var registry = new VariableRegistry();
		foreach (var variable in vars)
		{
			registry.Upsert(variable);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
		return renderer.Render(template, context);
	}

	private static VariableEntity Var(string name, VariableType type, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		Value = value
	};

	private static Func<VariableEntity> Cpu() => () => Var("cpu", VariableType.Numeric, "12.5");

	/// <summary>
	/// The case the whole unwrapping context exists for. Scriban's <c>ScriptBinaryExpression</c> dispatches
	/// a comparison on the CLR type of its operands before anything gets a chance to convert, and coerces
	/// an unrecognised operand through <c>ToInt</c>. A container that only overrode <c>ToBool</c>/
	/// <c>ToObject</c> - which is the mechanism ADR 0081's Context section names - reads 12.5 as 12 here and
	/// answers "not greater than 12" without throwing: a wrong answer that looks like a right one.
	/// </summary>
	[Test]
	public void A_fractional_comparison_is_not_truncated_to_an_integer()
		=> Both("{% if vars.cpu > 12 %}hot{% else %}cool{% endif %}", "hot", Cpu());

	// The two mirrors of the case above. Truncation is not the only wrong coercion available: rounding
	// 12.5 to 13 would answer the test above correctly and these two wrongly, so the three together pin
	// that the comparison is made against 12.5 itself rather than against any integer near it.
	[Test]
	public void The_same_comparison_against_the_value_itself_is_false()
		=> Both("{% if vars.cpu > 12.5 %}hot{% else %}cool{% endif %}", "cool", Cpu());

	[Test]
	public void The_mirrored_comparison_below_the_next_integer_is_true()
		=> Both("{% if vars.cpu < 13 %}cool{% else %}hot{% endif %}", "cool", Cpu());

	// The table in ADR 0081's Context section, rendered against a variable that actually carries an
	// attribute - which is the column the ADR labels "container, unwrapping context".
	[Test]
	public void The_adr_table_renders_as_documented()
	{
		var cpu = Var("cpu", VariableType.Numeric, "12.5");
		cpu.Unit = "%";
		var recording = Var("is_recording", VariableType.Boolean, "false");
		recording.Unit = "%";

		Assert.Multiple(() =>
		{
			Assert.That(Render("{{ vars.cpu }}", [cpu]), Is.EqualTo("12.5"));
			Assert.That(Render("{{ vars.cpu | round: 0 }}", [cpu]), Is.EqualTo("12"));
			Assert.That(Render("{% if vars.cpu > 10 %}hot{% endif %}", [cpu]), Is.EqualTo("hot"));
			Assert.That(Render("{% if vars.is_recording %}REC{% else %}idle{% endif %}", [recording]),
				Is.EqualTo("idle"));
			Assert.That(Render("{{ vars.cpu.unit }}", [cpu]), Is.EqualTo("%"));
		});
	}

	[Test]
	public void Every_typed_attribute_resolves_on_the_value_reference()
	{
		var cpu = Var("cpu", VariableType.Numeric, "12.5");
		cpu.Unit = "%";
		cpu.SemanticKind = "percentage";
		cpu.DecimalPlaces = 1;
		cpu.Min = 0;
		cpu.Max = 100;
		cpu.Step = 0.5;
		cpu.Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["sensor"] = "package" };

		var rendered = Render(
			"{{ vars.cpu }}|{{ vars.cpu.unit }}|{{ vars.cpu.semantic_kind }}|{{ vars.cpu.decimal_places }}" +
			"|{{ vars.cpu.min }}|{{ vars.cpu.max }}|{{ vars.cpu.step }}|{{ vars.cpu.sensor }}",
			[cpu]);

		Assert.That(rendered, Is.EqualTo("12.5|%|percentage|1|0|100|0.5|package"));
	}

	/// <summary>
	/// The open map is uninterpreted but it is not allowed to shadow an attribute the host computed - a
	/// provider that puts its own "unit" in the open map must not be able to contradict the typed one the
	/// Slider and the formatter read.
	/// </summary>
	[Test]
	public void The_open_map_cannot_shadow_a_typed_attribute()
	{
		var cpu = Var("cpu", VariableType.Numeric, "12.5");
		cpu.Unit = "%";
		cpu.Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["unit"] = "hijacked" };

		Assert.That(Render("{{ vars.cpu.unit }}", [cpu]), Is.EqualTo("%"));
	}

	// Liquid's falsiness rule is that nil and false are false and everything else - including 0 and "" - is
	// true, so an object standing in for a boolean silently flips every one of these to the wrong branch.
	// Each construct reaches the value by a different route inside Scriban, which is why they are listed
	// out rather than collapsed to one.
	[Test]
	public void A_false_boolean_stays_falsy_in_every_liquid_construct()
	{
		static Func<VariableEntity> Flag() => () => Var("flag", VariableType.Boolean, "false");

		Both("{% if vars.flag %}on{% else %}off{% endif %}", "off", Flag());
		Both("{% unless vars.flag %}off{% endunless %}", "off", Flag());
		Both("{% if vars.flag and true %}on{% else %}off{% endif %}", "off", Flag());
		Both("{% case vars.flag %}{% when false %}off{% when true %}on{% endcase %}", "off", Flag());
	}

	/// <summary>
	/// Scriban's Liquid <c>default</c> substitutes on an empty string, not on <c>false</c> - so an empty
	/// text variable is the shape that tells the two apart. A container reaching the filter unsubstituted
	/// renders as its own empty <c>ToString</c> instead of the fallback, which looks like a value rather
	/// than like a missing one.
	/// </summary>
	[Test]
	public void The_default_filter_sees_the_value_a_bare_variable_would_have_given_it()
	{
		Both("{{ vars.note | default: \"fallback\" }}",
			"fallback",
			() => Var("note", VariableType.Text, string.Empty));
		Both("{{ vars.note | default: \"fallback\" }}", "hello", () => Var("note", VariableType.Text, "hello"));
	}

	[Test]
	public void A_true_boolean_stays_truthy()
	{
		static Func<VariableEntity> Flag() => () => Var("flag", VariableType.Boolean, "true");

		Both("{% if vars.flag %}on{% else %}off{% endif %}", "on", Flag());
		Both("{% unless vars.flag %}off{% endunless %}", string.Empty, Flag());
	}

	[Test]
	public void A_comparison_with_a_variable_on_both_sides_compares_the_two_values()
	{
		static Func<VariableEntity> Left() => () => Var("used", VariableType.Numeric, "12.5");
		static Func<VariableEntity> Right() => () => Var("limit", VariableType.Numeric, "12");

		Both("{% if vars.used > vars.limit %}over{% else %}under{% endif %}", "over", Left(), Right());
		Both("{% if vars.limit > vars.used %}over{% else %}under{% endif %}", "under", Left(), Right());
	}

	[Test]
	public void A_non_empty_value_is_not_empty()
	{
		Both("{% if vars.note == empty %}blank{% else %}filled{% endif %}",
			"filled",
			() => Var("note", VariableType.Text, "hello"));
		Both("{% if vars.cpu == empty %}blank{% else %}filled{% endif %}", "filled", Cpu());
	}

	[Test]
	public void A_for_loop_iterates_the_value_and_not_the_container()
		=> Both("{% for item in vars.tags | split: \",\" %}[{{ item }}]{% endfor %}",
			"[a][b][c]",
			() => Var("tags", VariableType.Text, "a,b,c"));

	/// <summary>
	/// The broad transparency sweep. Every construct here reaches the value by a different route inside
	/// Scriban - a filter argument, a comparison, an assignment, a capture, a truth test - and each one is a
	/// route that decides on the CLR type before the context is asked to convert. One template that answers
	/// differently with an attribute than without is the whole regression this catches.
	/// </summary>
	[TestCaseSource(nameof(_transparentText))]
	[TestCaseSource(nameof(_transparentNumeric))]
	[TestCaseSource(nameof(_transparentBoolean))]
	public void An_attribute_changes_no_answer(VariableType type, string value, string template)
	{
		var bare = Render(template, [Var("x", type, value)]);
		var attributed = Attributed(template, Var("x", type, value));

		Assert.That(attributed, Is.EqualTo(bare));
	}

	private static IEnumerable<TestCaseData> _transparentText()
		=> Cases(VariableType.Text,
			"abc",
			"{{ vars.x }}",
			"{{ vars.x | upcase }}",
			"{{ vars.x | size }}",
			"{{ vars.x | append: '!' }}",
			"{{ vars.x | truncate: 2 }}",
			"{{ vars.x | escape }}",
			"{{ vars.x | slice: 0, 2 }}",
			"{{ vars.x | split: 'b' | join: '/' }}",
			"{{ vars.x | plain_text }}",
			"{% assign y = vars.x %}{{ y }}",
			"{% capture c %}{{ vars.x }}{% endcapture %}{{ c }}",
			"{% if vars.x contains 'b' %}Y{% else %}N{% endif %}",
			"{% if vars.x == 'abc' %}Y{% else %}N{% endif %}",
			"{% if vars.x != 'abc' %}Y{% else %}N{% endif %}",
			"{% if vars.x > 'a' %}Y{% else %}N{% endif %}",
			"{% if vars.x %}T{% else %}F{% endif %}");

	private static IEnumerable<TestCaseData> _transparentNumeric()
		=> Cases(VariableType.Numeric,
			"12.5",
			"{{ vars.x }}",
			"{{ vars.x | round: 0 }}",
			"{{ vars.x | plus: 1 }}",
			"{{ vars.x | minus: 0.5 }}",
			"{{ vars.x | times: 2 }}",
			"{{ vars.x | divided_by: 2 }}",
			"{{ vars.x | ceil }}",
			"{{ vars.x | floor }}",
			"{{ vars.x | abs }}",
			"{{ 100 | minus: vars.x }}",
			"{% assign y = vars.x | plus: 1 %}{{ y }}",
			"{% if vars.x > 12 %}Y{% else %}N{% endif %}",
			"{% if vars.x == 12.5 %}Y{% else %}N{% endif %}",
			"{% if vars.x >= 12.5 %}Y{% else %}N{% endif %}",
			"{% if vars.x %}T{% else %}F{% endif %}",
			"{% if vars.x == empty %}E{% else %}N{% endif %}");

	private static IEnumerable<TestCaseData> _transparentBoolean()
		=> Cases(VariableType.Boolean,
			"false",
			"{{ vars.x }}",
			"{{ vars.x | default: 'd' }}",
			"{% if vars.x %}T{% else %}F{% endif %}",
			"{% unless vars.x %}U{% endunless %}",
			"{% if vars.x == false %}Y{% else %}N{% endif %}",
			"{% if vars.x and true %}Y{% else %}N{% endif %}",
			"{% if vars.x or true %}Y{% else %}N{% endif %}",
			"{% case vars.x %}{% when false %}off{% when true %}on{% endcase %}",
			"{% if vars.x == empty %}E{% else %}N{% endif %}");

	private static IEnumerable<TestCaseData> Cases(VariableType type, string value, params string[] templates)
		=> templates.Select(template => new TestCaseData(type, value, template).SetArgDisplayNames(type.ToString(),
			value,
			template));

	/// <summary>
	/// An attribute a variable does not declare is absent from the container rather than present-and-empty.
	/// It matters because Liquid counts 0 as true: a <c>Max</c> defaulted to zero instead of left out would
	/// make <c>{% if vars.x.max %}</c> answer "yes, this variable has an upper bound" for every variable
	/// there is.
	/// </summary>
	[Test]
	public void An_undeclared_attribute_is_absent_rather_than_a_zero()
	{
		var cpu = Var("cpu", VariableType.Numeric, "12.5");
		cpu.Unit = "%";

		Assert.Multiple(() =>
		{
			Assert.That(Render("{% if vars.cpu.max %}bounded{% else %}unbounded{% endif %}", [cpu]),
				Is.EqualTo("unbounded"));
			Assert.That(Render("{{ vars.cpu.max }}", [cpu]), Is.Empty);
		});
	}

	/// <summary>
	/// A provider being momentarily quiet does not retract the unit it declared, so the value reads as the
	/// unavailable placeholder while the attribute path still answers.
	/// </summary>
	[Test]
	public void An_unavailable_attributed_variable_still_resolves_its_attributes()
	{
		var cpu = Var("cpu", VariableType.Numeric, "12.5");
		cpu.Unit = "%";

		var registry = new VariableRegistry();
		registry.Upsert(cpu);
		registry.SetAvailable(cpu.Id, false);

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("{{ vars.cpu }}", context),
				Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));
			Assert.That(renderer.Render("{{ vars.cpu.unit }}", context), Is.EqualTo("%"));
		});
	}

	[Test]
	public void Attributes_survive_the_event_and_input_overlays()
	{
		var cpu = Var("cpu", VariableType.Numeric, "12.5");
		cpu.Unit = "%";

		var registry = new VariableRegistry();
		registry.Upsert(cpu);
		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		var withEvent = context.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["channel"] = "general"
		});
		var withInputs = context.WithInputs(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["threshold"] = 10
		});

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("{{ vars.cpu.unit }}{{ event.channel }}", withEvent), Is.EqualTo("%general"));
			Assert.That(renderer.Render("{% if vars.cpu > 12 %}hot{% endif %}", withEvent), Is.EqualTo("hot"));

			Assert.That(renderer.Render("{{ vars.cpu.unit }}{{ vars.threshold }}", withInputs), Is.EqualTo("%10"));
			Assert.That(renderer.Render("{% if vars.cpu > vars.threshold %}hot{% endif %}", withInputs),
				Is.EqualTo("hot"));
		});
	}
}
