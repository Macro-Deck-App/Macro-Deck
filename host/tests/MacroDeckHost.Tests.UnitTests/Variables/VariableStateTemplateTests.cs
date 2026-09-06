using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// <c>vars.x.state</c> in Liquid templates: the same four cells the condition state operators answer,
/// reachable as <c>is_available</c>, <c>is_not_available</c>, <c>is_empty</c>, <c>is_not_empty</c>. Most
/// cases here render one template twice - once against a bare variable, once against the same variable
/// with an attribute - and assert both produce the same literal, because that pairing is what catches an
/// implementation that only hangs <c>state</c> off the previously-attributed wrapper.
/// </summary>
[TestFixture]
public class VariableStateTemplateTests
{
	private const string StateTemplate =
		"{{ vars.artist.state.is_available }}|{{ vars.artist.state.is_not_available }}|" +
		"{{ vars.artist.state.is_empty }}|{{ vars.artist.state.is_not_empty }}";

	private static VariableEntity Var(string name, VariableType type, string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		Value = value
	};

	private static VariableEntity StaleArtist(bool attributed)
	{
		var entity = new VariableEntity
		{
			Id = Guid.CreateVersion7(),
			Name = "artist",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = "com.example.music",
			DefinitionId = "declared-var",
			Value = "Radiohead",
			UpdateMode = VariableUpdateMode.Polled,
			UpdatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5),
			CreatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5)
		};

		if (attributed)
		{
			entity.Unit = "%";
		}

		return entity;
	}

	private static string Render(string template, params VariableEntity[] vars)
	{
		var registry = new VariableRegistry();
		foreach (var v in vars)
		{
			registry.Upsert(v);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
		return renderer.Render(template, context);
	}

	private static void Both(string template, string expected, Func<bool, VariableEntity> factory)
	{
		Assert.Multiple(() =>
		{
			Assert.That(Render(template, factory(false)), Is.EqualTo(expected), "bare");
			Assert.That(Render(template, factory(true)), Is.EqualTo(expected), "attributed");
		});
	}

	// C1 - available/non-empty, available/empty, unavailable (a polled integration variable past its
	// freshness window), each run bare and with a "%" unit attribute.
	[Test]
	public void An_available_non_empty_variable_reports_available_and_not_empty()
		=> Both(StateTemplate,
			"true|false|false|true",
			attributed =>
			{
				var v = Var("artist", VariableType.Text, "Radiohead");
				if (attributed)
				{
					v.Unit = "%";
				}

				return v;
			});

	[Test]
	public void An_available_empty_valued_variable_reports_available_and_empty()
		=> Both(StateTemplate,
			"true|false|true|false",
			attributed =>
			{
				var v = Var("artist", VariableType.Text, string.Empty);
				if (attributed)
				{
					v.Unit = "%";
				}

				return v;
			});

	[Test]
	public void An_unavailable_variable_reports_not_available_and_neither_empty_nor_not_empty()
		=> Both(StateTemplate, "false|true|false|false", StaleArtist);

	// C2 - an undeclared name.
	[Test]
	public void An_undeclared_name_reports_not_available_and_neither_empty_nor_not_empty()
	{
		var rendered = Render("{{ vars.nope.state.is_not_available }}|{{ vars.nope.state.is_available }}|" +
			"{{ vars.nope.state.is_empty }}|{{ vars.nope.state.is_not_empty }}");

		Assert.That(rendered, Is.EqualTo("true|false|false|false"));
	}

	[Test]
	public void An_undeclared_names_state_is_reachable_from_an_if_block()
	{
		var rendered = Render("{% if vars.nope.state.is_not_available %}gone{% else %}here{% endif %}");

		Assert.That(rendered, Is.EqualTo("gone"));
	}

	[Test]
	public void An_undeclared_name_stays_falsy_and_renders_as_nothing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Render("{% if vars.nope %}yes{% else %}no{% endif %}"), Is.EqualTo("no"));
			Assert.That(Render("[{{ vars.nope }}]"), Is.EqualTo("[]"));
		});
	}

	// C3 - "state" is reserved and cannot be shadowed by a provider's open attribute map.
	[Test]
	public void State_is_reserved_and_is_not_shadowed_by_an_open_attribute_of_the_same_name()
	{
		var artist = Var("artist", VariableType.Text, "Radiohead");
		artist.Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["state"] = "playing" };

		Assert.Multiple(() =>
		{
			Assert.That(Render("{{ vars.artist.state.is_not_empty }}", artist), Is.EqualTo("true"));
			Assert.That(Render("{{ vars.artist.state }}", artist), Is.Not.EqualTo("playing"));
		});
	}

	// C4 - existing-behaviour guard: rendering the bare value is unchanged, and the container stays
	// transparent for a member it does not own.
	[Test]
	public void Bare_value_rendering_is_unchanged_across_the_three_states()
	{
		var available = Var("artist", VariableType.Text, "Radiohead");
		var empty = Var("artist", VariableType.Text, string.Empty);

		Assert.Multiple(() =>
		{
			Assert.That(Render("{{ vars.artist }}", available), Is.EqualTo("Radiohead"));
			Assert.That(Render("{{ vars.artist }}", empty), Is.EqualTo(string.Empty));
			Assert.That(Render("{{ vars.artist }}", StaleArtist(attributed: false)),
				Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));
		});
	}

	[Test]
	public void An_unavailable_attributed_variable_still_resolves_its_unit()
		=> Assert.That(Render("{{ vars.artist.unit }}", StaleArtist(attributed: true)), Is.EqualTo("%"));

	// If this one fails, report it - do not adjust it. It proves the container delegates a member it does
	// not own (a plain string's "size") back to the accessor Scriban would have used on the bare value,
	// rather than swallowing it.
	[Test]
	public void An_unattributed_text_variable_still_delegates_an_unknown_member_to_the_string_accessor()
	{
		var artist = Var("artist", VariableType.Text, "Radiohead");

		Assert.That(Render("{{ vars.artist.size }}", artist), Is.EqualTo("9"));
	}

	// C5 - the same twelve cells declared once, evaluated through both surfaces: the condition state
	// operator and the "{{ vars.x.state.<member> }}" template member. Both must equal the declared literal
	// and each other.
	private static readonly (string State, string Op, string Member, bool Expected)[] _crossSurfaceCells =
	[
		("unavailable", "isEmpty", "is_empty", false),
		("unavailable", "isNotEmpty", "is_not_empty", false),
		("unavailable", "isAvailable", "is_available", false),
		("unavailable", "isNotAvailable", "is_not_available", true),
		("empty", "isEmpty", "is_empty", true),
		("empty", "isNotEmpty", "is_not_empty", false),
		("empty", "isAvailable", "is_available", true),
		("empty", "isNotAvailable", "is_not_available", false),
		("value", "isEmpty", "is_empty", false),
		("value", "isNotEmpty", "is_not_empty", true),
		("value", "isAvailable", "is_available", true),
		("value", "isNotAvailable", "is_not_available", false)
	];

	private static IEnumerable<TestCaseData> _crossSurfaceCellSource()
		=> _crossSurfaceCells.Select(cell => new TestCaseData(cell).SetArgDisplayNames($"{cell.State}_{cell.Op}"));

	[TestCaseSource(nameof(_crossSurfaceCellSource))]
	public void Condition_and_template_agree_on_every_state_cell(
		(string State, string Op, string Member, bool Expected) cell)
	{
		var registry = new VariableRegistry();
		var entity = cell.State switch
		{
			"value" => Var("x", VariableType.Text, "Radiohead"),
			"empty" => Var("x", VariableType.Text, string.Empty),
			_ => null
		};

		if (entity is not null)
		{
			registry.Upsert(entity);
		}

		var renderer = new VariableTemplateRenderer(registry);
		var evaluator = new ActionConditionEvaluator(renderer);
		var context = renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		using var left = JsonDocument.Parse("""{"$var":"x"}""");
		using var right = JsonDocument.Parse("null");
		var conditionResult = evaluator.Evaluate(left.RootElement, cell.Op, right.RootElement, context);

		var rendered = renderer.Render("{{ vars.x.state." + cell.Member + " }}", context);
		var templateResult = bool.Parse(rendered);

		Assert.Multiple(() =>
		{
			Assert.That(conditionResult, Is.EqualTo(cell.Expected), "condition");
			Assert.That(templateResult, Is.EqualTo(cell.Expected), "template");
			Assert.That(templateResult, Is.EqualTo(conditionResult), "cross-surface agreement");
		});
	}
}
