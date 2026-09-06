using MacroDeck.Localization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// Run twice: once over bare variables, once over variables carrying an attribute. Every expected string
/// is the same in both runs, which is the point - an attributed variable is wrapped in a
/// <see cref="VariableTemplateValue"/> container, and this whole filter catalog is what proves the
/// container stays transparent. Without the attributed run the catalog would exercise only the unwrapped
/// path, exactly where a container bug lands.
/// </summary>
[TestFixture(false)]
[TestFixture(true)]
public class VariableTemplateRendererFilterTests
{
	private readonly bool _attributed;

	public VariableTemplateRendererFilterTests(bool attributed)
	{
		_attributed = attributed;
	}

	private VariableEntity Var(string name, VariableType type, string value, int? decimalPlaces = null) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		Value = value,
		DecimalPlaces = decimalPlaces,
		Unit = _attributed ? "%" : null
	};

	private static (VariableRegistry Registry, VariableTemplateRenderer Renderer, VariableContext Context) Build(
		params VariableEntity[] vars)
	{
		var registry = new VariableRegistry();
		foreach (var v in vars)
		{
			registry.Upsert(v);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
		return (registry, renderer, context);
	}

	[Test]
	public void Issue_290_styled_variable_folds_through_the_filter()
	{
		var styled
			= "\U0001D49E\U0001D4A5\U0001D43B\U0001D49C\U0001D49E\U0001D4A6\U0001D438\U0001D445\U0001D4B4\U0001D4AF";
		var (_, renderer, context) = Build(Var("guild", VariableType.Text, styled));

		var result = renderer.Render("Server: {{ vars.guild | plain_text }}", context);

		Assert.That(result, Is.EqualTo("Server: CJHACKERYT"));
	}

	[Test]
	public void A_display_name_never_becomes_a_liquid_alias()
	{
		var variable = Var("obs_mac_cpu_usage", VariableType.Numeric, "42");
		variable.Presentation = new VariablePresentation(LocalizedText.FromLiteral("CPU usage"), null, default);
		var (_, renderer, context) = Build(variable);

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("{{ vars.obs_mac_cpu_usage }}", context), Is.EqualTo("42"));
			Assert.That(renderer.Render("{{ vars.CPU_usage }}", context), Is.Empty);
		});
	}

	[Test]
	public void Repeated_renders_of_the_same_cached_template_agree()
	{
		var styled
			= "\U0001D49E\U0001D4A5\U0001D43B\U0001D49C\U0001D49E\U0001D4A6\U0001D438\U0001D445\U0001D4B4\U0001D4AF";
		var (_, renderer, context) = Build(Var("guild", VariableType.Text, styled));
		const string template = "Server: {{ vars.guild | plain_text }}";

		var first = renderer.Render(template, context);
		var second = renderer.Render(template, context);

		Assert.That(first, Is.EqualTo("Server: CJHACKERYT"));
		Assert.That(second, Is.EqualTo(first));
	}

	[Test]
	public void Boolean_variable_renders_lowercase_true_with_or_without_the_filter()
	{
		var (_, renderer, context) = Build(Var("flag", VariableType.Boolean, "true"));

		Assert.That(renderer.Render("{{ vars.flag }}", context), Is.EqualTo("true"));
		Assert.That(renderer.Render("{{ vars.flag | plain_text }}", context), Is.EqualTo("true"));
	}

	[Test]
	public void Numeric_variable_keeps_its_configured_decimal_places_through_the_filter()
	{
		var (_, renderer, context) = Build(Var("amount", VariableType.Numeric, "12.5", decimalPlaces: 2));

		Assert.That(renderer.Render("{{ vars.amount | plain_text }}", context), Is.EqualTo("12.50"));
	}

	[Test]
	public void Unavailable_placeholder_survives_the_filter()
	{
		var registry = new VariableRegistry();
		var variable = Var("offline", VariableType.Text, "styled value");
		registry.Upsert(variable);
		registry.SetAvailable(variable.Id, false);

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		Assert.That(renderer.Render("{{ vars.offline | plain_text }}", context),
			Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));
	}

	[Test]
	public void Event_parameter_folds_through_the_filter()
	{
		var (_, renderer, context) = Build();
		var withEvent = context.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["channel"] = "\uFF21\uFF22\uFF23"
		});

		Assert.That(renderer.Render("{{ event.channel | plain_text }}", withEvent), Is.EqualTo("ABC"));
	}

	[Test]
	public void Null_event_parameter_renders_empty_without_throwing()
	{
		var (_, renderer, context) = Build();
		var withEvent = context.WithEvent(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["channel"] = null
		});

		string? result = null;
		Assert.DoesNotThrow(() => result = renderer.Render("{{ event.channel | plain_text }}", withEvent));
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Renders_against_the_empty_context()
	{
		var renderer = new VariableTemplateRenderer(new VariableRegistry());

		Assert.That(renderer.Render("{{ 'x' | plain_text }}", VariableContext.Empty), Is.EqualTo("x"));
	}

	[Test]
	public void Upcase_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello"));

		Assert.That(renderer.Render("{{ vars.word | upcase }}", context), Is.EqualTo("HELLO"));
	}

	[Test]
	public void Downcase_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "HELLO"));

		Assert.That(renderer.Render("{{ vars.word | downcase }}", context), Is.EqualTo("hello"));
	}

	[Test]
	public void Capitalize_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello world"));

		Assert.That(renderer.Render("{{ vars.word | capitalize }}", context), Is.EqualTo("Hello world"));
	}

	[Test]
	public void Truncate_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word",
			VariableType.Text,
			"This sentence is definitely longer than twenty characters"));

		var result = renderer.Render("{{ vars.word | truncate: 20 }}", context);

		Assert.That(result, Has.Length.EqualTo(20));
		Assert.That(result, Does.EndWith("..."));
	}

	[Test]
	public void Default_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ nothing | default: \"-\" }}", context), Is.EqualTo("-"));
	}

	[Test]
	public void Round_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ 3.14159 | round: 2 }}", context), Is.EqualTo("3.14"));
	}

	[Test]
	public void Plus_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ 41 | plus: 1 }}", context), Is.EqualTo("42"));
	}

	[Test]
	public void Date_chip_renders()
	{
		var (_, renderer, context) = Build(Var("day", VariableType.Text, "2016/01/05"));

		Assert.That(renderer.Render("{{ vars.day | date: \"%Y-%m-%d\" }}", context), Is.EqualTo("2016-01-05"));
	}

	[Test]
	public void Assign_still_works_after_the_push_order_change()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{% assign x = 1 %}{{ x }}", context), Is.EqualTo("1"));
	}

	[Test]
	public void Unknown_root_member_renders_empty_rather_than_throwing()
	{
		var (_, renderer, context) = Build();

		string? result = null;
		Assert.DoesNotThrow(() => result = renderer.Render("{{ unknownRoot.member }}", context));
		Assert.That(result, Is.Empty);
	}

	// Template builder catalog (issue #238) - every entry in
	// ui/angular/.../template-builder/liquid-snippets.ts must have a matching case here proving it
	// actually renders through Scriban's Liquid-compat mode; a catalog entry with no passing case
	// here must be cut from the catalog rather than shipped.

	[Test]
	public void Strip_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "  hello  "));

		Assert.That(renderer.Render("{{ vars.word | strip }}", context), Is.EqualTo("hello"));
	}

	[Test]
	public void Minus_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ 41 | minus: 1 }}", context), Is.EqualTo("40"));
	}

	[Test]
	public void Times_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ 21 | times: 2 }}", context), Is.EqualTo("42"));
	}

	[Test]
	public void DividedBy_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ 84 | divided_by: 2 }}", context), Is.EqualTo("42"));
	}

	[Test]
	public void Floor_chip_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{{ 3.7 | floor }}", context), Is.EqualTo("3"));
	}

	[Test]
	public void Split_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | size }}", context), Is.EqualTo("3"));
	}

	[Test]
	public void Join_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | join: \", \" }}", context), Is.EqualTo("a, b, c"));
	}

	[Test]
	public void First_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | first }}", context), Is.EqualTo("a"));
	}

	[Test]
	public void Last_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | last }}", context), Is.EqualTo("c"));
	}

	[Test]
	public void Size_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello"));

		Assert.That(renderer.Render("{{ vars.word | size }}", context), Is.EqualTo("5"));
	}

	[Test]
	public void Sort_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "c,a,b"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | sort | join: \",\" }}", context),
			Is.EqualTo("a,b,c"));
	}

	[Test]
	public void Uniq_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,a,c"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | uniq | join: \",\" }}", context),
			Is.EqualTo("a,b,c"));
	}

	[Test]
	public void Contains_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));

		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | contains: \"b\" }}", context), Is.EqualTo("true"));
	}

	[Test]
	public void Replace_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello world"));

		Assert.That(renderer.Render("{{ vars.word | replace: \"o\", \"0\" }}", context), Is.EqualTo("hell0 w0rld"));
	}

	[Test]
	public void ReplaceFirst_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello world"));

		Assert.That(renderer.Render("{{ vars.word | replace_first: \"o\", \"0\" }}", context),
			Is.EqualTo("hell0 world"));
	}

	[Test]
	public void Remove_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello world"));

		Assert.That(renderer.Render("{{ vars.word | remove: \"l\" }}", context), Is.EqualTo("heo word"));
	}

	[Test]
	public void IfElse_snippet_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{% if true %}yes{% else %}no{% endif %}", context), Is.EqualTo("yes"));
	}

	[Test]
	public void Unless_snippet_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{% unless false %}yes{% endunless %}", context), Is.EqualTo("yes"));
	}

	[Test]
	public void For_snippet_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));

		var result = renderer.Render("{% for item in vars.tags | split: \",\" %}{{ item }}{% endfor %}", context);

		Assert.That(result, Is.EqualTo("abc"));
	}

	[Test]
	public void CaseWhen_snippet_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "a"));

		var result = renderer.Render("{% case vars.word %}{% when \"a\" %}first{% else %}other{% endcase %}", context);

		Assert.That(result, Is.EqualTo("first"));
	}

	[Test]
	public void Assign_snippet_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{% assign name = 42 %}{{ name }}", context), Is.EqualTo("42"));
	}

	[Test]
	public void Capture_snippet_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("{% capture name %}hello{% endcapture %}{{ name }}", context), Is.EqualTo("hello"));
	}

	[Test]
	public void Comment_snippet_renders()
	{
		var (_, renderer, context) = Build();

		Assert.That(renderer.Render("before{% comment %}hidden{% endcomment %}after", context),
			Is.EqualTo("beforeafter"));
	}

	// The math filters and the comparison operators are the ones a numeric variable is actually used
	// with, and every other test in this file exercises them against bare literals - which passes
	// whether or not a variable reaches Scriban as a number. These go through a real Numeric
	// variable on purpose.
	[Test]
	public void Numeric_variable_arithmetic_is_arithmetic_and_not_string_concatenation()
	{
		var (_, renderer, context) = Build(Var("price", VariableType.Numeric, "12.00", 2));

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("{{ vars.price | plus: 3 }}", context), Is.EqualTo("15.00"));
			Assert.That(renderer.Render("{{ vars.price | times: 2 }}", context), Is.EqualTo("24.00"));
			Assert.That(renderer.Render("{{ vars.price | minus: 2 }}", context), Is.EqualTo("10.00"));
			// Division does not carry the operand's scale through, so this is 6 rather than 6.00 -
			// still arithmetic, which is what the assertion is about.
			Assert.That(renderer.Render("{{ vars.price | divided_by: 2 }}", context), Is.EqualTo("6"));
			Assert.That(renderer.Render("{{ vars.price | round: 0 }}", context), Is.EqualTo("12"));
			Assert.That(renderer.Render("{{ vars.price | floor }}", context), Is.EqualTo("12"));
		});
	}

	[Test]
	public void Numeric_variable_compares_by_magnitude_and_not_by_text()
	{
		var (_, renderer, context) = Build(Var("price", VariableType.Numeric, "12.00", 2));

		Assert.Multiple(() =>
		{
			// "12.00" sorts before "5" as text, so a string comparison answers this one wrongly.
			Assert.That(renderer.Render("{% if vars.price > 5 %}yes{% else %}no{% endif %}", context),
				Is.EqualTo("yes"));
			Assert.That(renderer.Render("{% if vars.price < 100 %}yes{% else %}no{% endif %}", context),
				Is.EqualTo("yes"));
		});
	}

	[Test]
	public void Numeric_variable_still_renders_with_its_declared_decimal_places()
	{
		var (_, renderer, context) = Build(Var("price", VariableType.Numeric, "12", 2),
			Var("count", VariableType.Numeric, "7"));

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("{{ vars.price }}", context), Is.EqualTo("12.00"));
			Assert.That(renderer.Render("{{ vars.count }}", context), Is.EqualTo("7"));
		});
	}

	[Test]
	public void Append_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hi"));
		Assert.That(renderer.Render("{{ vars.word | append: \"!\" }}", context), Is.EqualTo("hi!"));
	}

	[Test]
	public void Prepend_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hi"));
		Assert.That(renderer.Render("{{ vars.word | prepend: \">> \" }}", context), Is.EqualTo(">> hi"));
	}

	[Test]
	public void RemoveFirst_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello world"));
		Assert.That(renderer.Render("{{ vars.word | remove_first: \"l\" }}", context), Is.EqualTo("helo world"));
	}

	[Test]
	public void Lstrip_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "  hi  "));
		Assert.That(renderer.Render("[{{ vars.word | lstrip }}]", context), Is.EqualTo("[hi  ]"));
	}

	[Test]
	public void Rstrip_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "  hi  "));
		Assert.That(renderer.Render("[{{ vars.word | rstrip }}]", context), Is.EqualTo("[  hi]"));
	}

	[Test]
	public void StripHtml_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "<b>hi</b>"));
		Assert.That(renderer.Render("{{ vars.word | strip_html }}", context), Is.EqualTo("hi"));
	}

	[Test]
	public void StripNewlines_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "a\nb"));
		Assert.That(renderer.Render("{{ vars.word | strip_newlines }}", context), Is.EqualTo("ab"));
	}

	[Test]
	public void Truncatewords_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "one two three four five"));
		Assert.That(renderer.Render("{{ vars.word | truncatewords: 3 }}", context), Is.EqualTo("one two three..."));
	}

	[Test]
	public void Ceil_chip_renders()
	{
		var (_, renderer, context) = Build();
		Assert.That(renderer.Render("{{ 3.2 | ceil }}", context), Is.EqualTo("4"));
	}

	[Test]
	public void Modulo_chip_renders()
	{
		var (_, renderer, context) = Build();
		Assert.That(renderer.Render("{{ 10 | modulo: 3 }}", context), Is.EqualTo("1"));
	}

	[Test]
	public void Abs_chip_renders()
	{
		var (_, renderer, context) = Build();
		Assert.That(renderer.Render("{{ -5 | abs }}", context), Is.EqualTo("5"));
	}

	[Test]
	public void Escape_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "<b>"));
		Assert.That(renderer.Render("{{ vars.word | escape }}", context), Is.EqualTo("&lt;b&gt;"));
	}

	[Test]
	public void Reverse_chip_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));
		Assert.That(renderer.Render("{{ vars.tags | split: \",\" | reverse | join: \",\" }}", context),
			Is.EqualTo("c,b,a"));
	}

	[Test]
	public void Break_snippet_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));
		var result = renderer.Render(
			"{% for item in vars.tags | split: \",\" %}{% if item == \"b\" %}{% break %}{% endif %}{{ item }}{% endfor %}",
			context);
		Assert.That(result, Is.EqualTo("a"));
	}

	[Test]
	public void Continue_snippet_renders()
	{
		var (_, renderer, context) = Build(Var("tags", VariableType.Text, "a,b,c"));
		var result = renderer.Render(
			"{% for item in vars.tags | split: \",\" %}{% if item == \"b\" %}{% continue %}{% endif %}{{ item }}{% endfor %}",
			context);
		Assert.That(result, Is.EqualTo("ac"));
	}

	[Test]
	public void Raw_snippet_renders()
	{
		var (_, renderer, context) = Build();
		var result = renderer.Render("{% raw %}{{ not evaluated }}{% endraw %}", context);
		Assert.That(result, Is.EqualTo("{{ not evaluated }}"));
	}

	[Test]
	public void Ifchanged_snippet_renders()
	{
		var (_, renderer, context) = Build(Var("letters", VariableType.Text, "a,a,b,b,c"));
		var result = renderer.Render(
			"{% for item in vars.letters | split: \",\" %}{% ifchanged %}{{ item }}{% endifchanged %}{% endfor %}",
			context);
		Assert.That(result, Is.EqualTo("abc"));
	}

	[Test]
	public void StringMd5_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello"));
		var result = renderer.Render("{{ vars.word | string.md5 }}", context);
		Assert.That(result, Is.EqualTo("5d41402abc4b2a76b9719d911017c592"));
	}

	[Test]
	public void StringBase64Encode_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hi"));
		var result = renderer.Render("{{ vars.word | string.base64_encode }}", context);
		Assert.That(result, Is.EqualTo("aGk="));
	}

	[Test]
	public void StringPadLeft_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "7"));
		var result = renderer.Render("{{ vars.word | string.pad_left 5 }}", context);
		Assert.That(result, Is.EqualTo("    7"));
	}

	[Test]
	public void StringIndexOf_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hello"));
		var result = renderer.Render("{{ vars.word | string.index_of \"l\" }}", context);
		Assert.That(result, Is.EqualTo("2"));
	}

	[Test]
	public void StringHandleize_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "My Title!"));
		var result = renderer.Render("{{ vars.word | string.handleize }}", context);
		Assert.That(result, Is.EqualTo("my-title"));
	}

	[Test]
	public void ObjectToJson_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "hi"));
		var result = renderer.Render("{{ vars.word | object.to_json }}", context);
		Assert.That(result, Is.EqualTo("\"hi\""));
	}

	[Test]
	public void HtmlUrlEncode_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "a b"));
		var result = renderer.Render("{{ vars.word | html.url_encode }}", context);
		Assert.That(result, Is.EqualTo("a%20b"));
	}

	[Test]
	public void HtmlNewlineToBr_chip_renders()
	{
		var (_, renderer, context) = Build(Var("word", VariableType.Text, "a\nb"));
		var result = renderer.Render("{{ vars.word | html.newline_to_br }}", context);
		Assert.That(result, Is.EqualTo("a<br />\nb"));
	}

	[Test]
	public void MathUuid_chip_renders()
	{
		var (_, renderer, context) = Build();
		var result = renderer.Render("{{ math.uuid }}", context);
		Assert.That(result, Does.Match(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"));
	}

	[Test]
	public void DateNow_chip_renders()
	{
		var (_, renderer, context) = Build();
		var result = renderer.Render("{{ date.now }}", context);
		Assert.That(result, Does.Match(@"^\d{1,2} \w{3} \d{4}$"));
	}
}
