using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Scriban;
using Scriban.Parsing;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableTemplateRendererReducedContextTests
{
	private static readonly string _widgetId = Guid.NewGuid().ToString();

	private static readonly string[] _templates =
	[
		"{{ vars.a }}",
		"{{ vars.a }} and {{ vars.b }}",
		"{{ vars.u }}{{ vars.u.unit }}",
		"{{ vars.a.state.is_available }}/{{ vars.gone.state.is_available }}",
		"{{ vars[\"a\"] }}",
		"{{ vars.a | upcase }}",
		"{{ vars.n | round: 1 }}",
		"{{ vars.empty_text | default: vars.b }}",
		"{{ vars.a | plain_text }}",
		"{% if vars.n > 3 %}{{ vars.b }}{% elsif vars.flag %}f{% else %}-{% endif %}",
		"{% if vars.flag %}yes{% endif %}",
		"{{ vars.typo }}|{{ vars.typo.state.is_not_available }}",
		"{{ vars.gone }}",
		"{{ vars.shared }}",
		"{{ vars.n }}",
		"{{ vars.attr.room }}",
		"{{ vars }}",
		"{% for v in vars %}[{{ v }}]{% endfor %}",
		"{{ vars == empty }}",
		"{{ object.keys vars }}",
		"{{ vars.a }}|{{ \"{{ vars.unrelated }}\" | object.eval_template }}",
		"{{ object[\"eval_template\"] }}",
		"{% assign k = \"unrelated\" %}{{ vars[k] }}",
		"{{ vars.tags | split: \",\" | array.size }}",
		"{% assign x = vars.a %}{{ x }}",
		"{% capture c %}{{ vars.a }}{% endcapture %}{{ c }}",
		"{% for t in vars.tags | split: \",\" %}{{ t }}{% endfor %}",
		"{{ event.x }}",
		"{{ vars.a ",
	];

	private static VariableEntity Var(
		string name,
		string value,
		VariableType type = VariableType.Text,
		VariableScope scope = VariableScope.Global,
		string? scopeRefId = null) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = scope,
		ScopeRefId = scopeRefId,
		Type = type,
		Classification = VariableClassification.User,
		Value = value,
		UpdatedAt = DateTime.UtcNow,
	};

	private static VariableRegistry Registry(params VariableEntity[] variables)
	{
		var registry = new VariableRegistry();
		foreach (var variable in variables)
		{
			registry.Upsert(variable);
		}

		return registry;
	}

	private static VariableRegistry Catalog()
	{
		var number = Var("n", "12.3456", VariableType.Numeric);
		number.DecimalPlaces = 2;
		var unit = Var("u", "40", VariableType.Numeric);
		unit.Unit = "%";
		var attributed = Var("attr", "on");
		attributed.Attributes = new Dictionary<string, string> { ["room"] = "kitchen" };
		var gone = Var("gone", "stale");

		var registry = Registry(
			Var("a", "Alpha"),
			Var("b", "Beta"),
			Var("empty_text", string.Empty),
			Var("flag", "true", VariableType.Boolean),
			Var("tags", "x,y"),
			Var("unrelated", "LEAKED"),
			Var("shared", "global shared"),
			Var("shared", "widget shared", scope: VariableScope.Widget, scopeRefId: _widgetId),
			Var("a", "widget alpha", scope: VariableScope.Widget, scopeRefId: _widgetId),
			number,
			unit,
			attributed,
			gone);

		registry.SetAvailable(gone.Id, false);
		return registry;
	}

	private static string Outcome(Func<string> render)
	{
		try
		{
			return render();
		}
		catch (Exception exception)
		{
			return "throws " + exception.GetType().Name;
		}
	}

	[TestCaseSource(nameof(_templates))]
	public async Task A_template_renders_the_same_text_as_it_does_against_the_full_context(string template)
	{
		foreach (var (scope, scopeRefId) in new[]
			{ (VariableScope.Global, (string?)null), (VariableScope.Widget, _widgetId) })
		{
			var registry = Catalog();
			var full = new VariableTemplateRenderer(registry);
			var context = await full.CreateContextAsync(scope, scopeRefId);

			var expected = Outcome(() => full.Render(template, context));
			var actual = Outcome(() => new VariableTemplateRenderer(registry).Render(template, scope, scopeRefId));

			Assert.That(actual, Is.EqualTo(expected), $"scope {scope}");
		}
	}

	[Test]
	public void A_template_follows_every_change_to_the_variable_it_names()
	{
		var live = Var("a", "one");
		var registry = Registry(live);
		var renderer = new VariableTemplateRenderer(registry);
		string Render() => renderer.Render("{{ vars.a }}|{{ vars.a.unit }}|{{ vars.a.room }}", VariableScope.Global, null);

		Assert.That(Render(), Is.EqualTo("one||"));

		live.Value = "two";
		Assert.That(Render(), Is.EqualTo("two||"), "a value written in place");

		live.Value = "one";
		Assert.That(Render(), Is.EqualTo("one||"), "a value written back");

		live.Unit = "%";
		Assert.That(Render(), Is.EqualTo("one|%|"), "a unit");

		live.Attributes = new Dictionary<string, string> { ["room"] = "hall" };
		Assert.That(Render(), Is.EqualTo("one|%|hall"), "an attribute");

		live.Attributes = new Dictionary<string, string> { ["room"] = "kitchen" };
		Assert.That(Render(), Is.EqualTo("one|%|kitchen"), "an attribute changed by content");

		registry.SetAvailable(live.Id, false);
		Assert.That(Render(), Does.StartWith(VariableTemplateRenderer.UnavailablePlaceholder), "availability");

		registry.Remove(live.Id);
		Assert.That(Render(), Is.EqualTo("||"), "deletion");
	}

	[Test]
	public void An_unchanged_variable_renders_the_same_text_every_time()
	{
		var renderer = new VariableTemplateRenderer(Registry(Var("a", "same")));

		var first = renderer.Render("{{ vars.a }}", VariableScope.Global, null);
		var second = renderer.Render("{{ vars.a }}", VariableScope.Global, null);

		Assert.That(second, Is.EqualTo(first).And.EqualTo("same"));
	}

	[Test]
	public void A_polled_variable_that_stops_reporting_reads_as_unavailable_without_any_write()
	{
		var live = Var("cpu", "42");
		live.Classification = VariableClassification.Integration;
		live.OwnerIntegrationId = "system";
		var renderer = new VariableTemplateRenderer(Registry(live));

		Assert.That(renderer.Render("{{ vars.cpu }}", VariableScope.Global, null), Is.EqualTo("42"));

		live.UpdatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(1);

		Assert.That(renderer.Render("{{ vars.cpu }}", VariableScope.Global, null),
			Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));
	}

	[Test]
	public void Two_templates_rendered_alternately_for_one_widget_each_show_their_own_text()
	{
		var renderer = new VariableTemplateRenderer(Registry(Var("a", "x")));

		for (var round = 0; round < 3; round++)
		{
			Assert.Multiple(() =>
			{
				Assert.That(renderer.Render("A {{ vars.a }}", VariableScope.Widget, _widgetId), Is.EqualTo("A x"));
				Assert.That(renderer.Render("B {{ vars.a }}", VariableScope.Widget, _widgetId), Is.EqualTo("B x"));
			});
		}
	}

	[Test]
	public void A_render_shows_the_variable_as_it_was_read_even_if_it_changes_while_rendering()
	{
		var live = Var("a", "v1");
		var renderer = new VariableTemplateRenderer(Registry(live));
		var snapshot = renderer.CaptureSnapshot(["a"], VariableScope.Global, null);

		live.Value = "v2";

		Assert.That(renderer.RenderSnapshot("{{ vars.a }}", snapshot), Is.EqualTo("v1"));

		live.Value = "v1";
		Assert.That(renderer.Render("{{ vars.a }}", VariableScope.Global, null), Is.EqualTo("v1"));

		live.Value = "v3";
		Assert.That(renderer.Render("{{ vars.a }}", VariableScope.Global, null), Is.EqualTo("v3"));
	}

	[TestCase("{{ vars.system_cpu_name }}", "system_cpu_name")]
	[TestCase("{{ vars.n | round: 1 }}", "n")]
	[TestCase("{{ vars.a | default: vars.b }}", "a,b")]
	[TestCase("{% if vars.a %}{{ vars.b }}{% elsif vars.c %}c{% else %}-{% endif %}", "a,b,c")]
	public void A_template_that_only_names_variables_reads_exactly_those(string template, string expected)
	{
		var names = TemplateVariableAccess.ReadNames(Parse(template));

		Assert.That(names, Is.EquivalentTo(expected.Split(',')));
	}

	[TestCase("{{ vars }}")]
	[TestCase("{{ \"{{ vars.b }}\" | object.eval_template }}")]
	[TestCase("{{ vars.a | date: \"%Y\" }}")]
	public void A_template_that_can_read_unnamed_variables_needs_the_full_context(string template)
	{
		Assert.That(TemplateVariableAccess.ReadNames(Parse(template)), Is.Null);
	}

	private static Template Parse(string template)
		=> Template.ParseLiquid(template, null, new ParserOptions { LiquidFunctionsToScriban = true });
}
