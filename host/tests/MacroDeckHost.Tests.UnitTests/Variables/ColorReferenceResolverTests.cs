using System.Text.Json.Nodes;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Ui.Config;
using System.Text.Json;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ColorReferenceResolverTests
{
	private static readonly Guid WidgetId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

	private VariableRegistry _registry = null!;
	private ColorReferenceResolver _resolver = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_resolver = new ColorReferenceResolver(_registry);
	}

	private VariableEntity Add(string name, VariableType type, string value, string? widgetId = null,
		bool available = true)
	{
		var entity = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = widgetId is null ? VariableScope.Global : VariableScope.Widget,
			ScopeRefId = widgetId,
			Type = type,
			Classification = VariableClassification.User,
			Value = value
		};
		_registry.Upsert(entity, available);
		return entity;
	}

	[Test]
	public void A_reference_resolves_through_its_modifiers_in_order()
	{
		Add("primary", VariableType.Color, "#3366ff");

		Assert.That(_resolver.Resolve("{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}"),
			Is.EqualTo("#003df5b3"));
	}

	[Test]
	public void A_mix_can_take_its_second_colour_from_another_variable()
	{
		Add("primary", VariableType.Color, "#ff0000");
		Add("other", VariableType.Color, "#0000ff");

		Assert.That(_resolver.Resolve("{{ vars.primary | color | color_mix: vars.other, 50 }}"),
			Is.EqualTo("#800080"));
	}

	[TestCase("{{ vars.missing | color }}")]
	[TestCase("{{ vars.label | color }}")]
	[TestCase("{{ vars.offline | color }}")]
	[TestCase("{{ vars.primary | color | color_mix: vars.missing, 50 }}")]
	public void A_reference_that_cannot_resolve_reads_as_unset(string reference)
	{
		Add("primary", VariableType.Color, "#3366ff");
		Add("label", VariableType.Text, "#3366ff");
		Add("offline", VariableType.Color, "#3366ff", available: false);

		Assert.That(_resolver.Resolve(reference), Is.Empty);
	}

	[TestCase("#3366ff")]
	[TestCase("{{ vars.primary }}")]
	[TestCase("Hello {{ vars.primary | color }}")]
	[TestCase("")]
	public void Anything_that_is_not_a_reference_passes_through(string value)
	{
		Add("primary", VariableType.Color, "#3366ff");

		Assert.That(_resolver.Resolve(value), Is.EqualTo(value));
	}

	[Test]
	public void Widget_data_resolves_every_reference_including_threshold_bands_and_leaves_labels()
	{
		Add("primary", VariableType.Color, "#3366ff");
		const string Data = """
			{
			  "label": "{{ vars.primary }}",
			  "backgroundColor": "{{ vars.primary | color | color_opacity: 50 }}",
			  "gauges": [
			    { "thresholds": { "bands": [
			      { "id": "a", "color": "{{ vars.primary | color }}" },
			      { "id": "b", "color": "#ff0000", "from": 50 } ] } }
			  ]
			}
			""";

		var resolved = JsonNode.Parse(_resolver.ResolveData(Data, null)!)!;

		Assert.Multiple(() =>
		{
			Assert.That(resolved["label"]!.GetValue<string>(), Is.EqualTo("{{ vars.primary }}"));
			Assert.That(resolved["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#3366ff80"));
			var bands = resolved["gauges"]![0]!["thresholds"]!["bands"]!;
			Assert.That(bands[0]!["color"]!.GetValue<string>(), Is.EqualTo("#3366ff"));
			Assert.That(bands[1]!["color"]!.GetValue<string>(), Is.EqualTo("#ff0000"));
		});
	}

	[Test]
	public void Data_without_a_reference_is_returned_untouched()
	{
		const string Data = """{"backgroundColor":"#3366ff","label":"{{ vars.primary }}"}""";

		Assert.That(_resolver.ResolveData(Data, WidgetId), Is.SameAs(Data));
	}

	[Test]
	public void A_widget_scoped_variable_wins_over_the_global_one_for_its_widget()
	{
		Add("primary", VariableType.Color, "#3366ff");
		Add("primary", VariableType.Color, "#ff0000", WidgetId.ToString());
		const string Data = """{"color":"{{ vars.primary | color }}"}""";

		Assert.Multiple(() =>
		{
			Assert.That(JsonNode.Parse(_resolver.ResolveData(Data, WidgetId)!)!["color"]!.GetValue<string>(),
				Is.EqualTo("#ff0000"));
			Assert.That(JsonNode.Parse(_resolver.ResolveData(Data, Guid.NewGuid())!)!["color"]!.GetValue<string>(),
				Is.EqualTo("#3366ff"));
		});
	}

	[Test]
	public void The_template_filters_render_the_same_colour_anywhere_liquid_renders()
	{
		Add("primary", VariableType.Color, "#3366ff");
		Add("other", VariableType.Color, "#ffffff");
		var renderer = new VariableTemplateRenderer(_registry);

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}",
					VariableScope.Global, null),
				Is.EqualTo("#003df5b3"));
			Assert.That(renderer.Render("{{ vars.primary | color | color_mix: vars.other, 50 }}",
					VariableScope.Global, null),
				Is.EqualTo("#99b3ff"));
			Assert.That(renderer.Render("[{{ vars.missing | color | color_lighten: 10 }}]", VariableScope.Global, null),
				Is.EqualTo("[]"));
		});
	}

	[Test]
	public void A_band_whose_variable_is_gone_loses_only_its_own_colour()
	{
		Add("warn", VariableType.Color, "#ffcc00");
		const string Data = """
			{ "thresholds": { "bands": [
			  { "id": "ok", "color": "#34c759" },
			  { "id": "warn", "color": "{{ vars.warn | color }}", "from": 50 },
			  { "id": "bad", "color": "{{ vars.deleted | color }}", "from": 90 } ] } }
			""";

		using var resolved = JsonDocument.Parse(_resolver.ResolveData(Data, null)!);
		var parsed = UiThresholds.TryParse(resolved.RootElement.GetProperty("thresholds"), out var thresholds);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(thresholds!.Bands.Select(band => band.From), Is.EqualTo(new double?[] { null, 50, 90 }));
			Assert.That(thresholds.ColorAt(10), Is.EqualTo("#34c759"));
			Assert.That(thresholds.ColorAt(60), Is.EqualTo("#ffcc00"));
			Assert.That(thresholds.ColorAt(95), Is.Null);
		});
	}

	[Test]
	public void The_colour_filters_take_a_variable_only_when_it_is_a_color_variable()
	{
		Add("primary", VariableType.Color, "#3366ff");
		Add("label", VariableType.Text, "#ff0000");
		var renderer = new VariableTemplateRenderer(_registry);

		Assert.Multiple(() =>
		{
			Assert.That(renderer.Render("[{{ vars.label | color }}]", VariableScope.Global, null), Is.EqualTo("[]"));
			Assert.That(renderer.Render("[{{ vars.label | color_darken: 10 }}]", VariableScope.Global, null),
				Is.EqualTo("[]"));
			Assert.That(renderer.Render("[{{ vars.primary | color | color_mix: vars.label, 50 }}]",
				VariableScope.Global, null), Is.EqualTo("[]"));
			Assert.That(renderer.Render("{{ vars.primary | color_darken: 20 }}", VariableScope.Global, null),
				Is.EqualTo("#003df5"));
			Assert.That(renderer.Render("{{ \"#3366ff\" | color_darken: 20 }}", VariableScope.Global, null),
				Is.EqualTo("#003df5"));
		});
	}

	[Test]
	public void The_opacity_steps_resolve_in_a_reference()
	{
		Add("primary", VariableType.Color, "#3366ffcc");

		Assert.That(_resolver.Resolve("{{ vars.primary | color | color_reduce_opacity: 20 | color_lighten: 10 }}"),
			Is.EqualTo("#4775ffa3"));
	}
}
