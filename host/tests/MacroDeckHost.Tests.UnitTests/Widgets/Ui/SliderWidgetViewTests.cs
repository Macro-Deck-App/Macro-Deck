using System.Text.Json;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Slider;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class SliderWidgetViewTests
{
	private static readonly IReadOnlyList<UiEventHandler> _boundEvents =
	[
		UiEventHandler.On(UiComponentEvents.Adjust, _ => UiEventOutcome.Accepted),
		UiEventHandler.On(UiComponentEvents.Change, _ => UiEventOutcome.Accepted),
	];

	private static readonly string[] _bothEventNames = ["adjust", "change"];

	private static readonly string[] _verticalChildIds = ["slider.header", "slider.track", "slider.value"];

	[Test]
	public void Label_and_value_are_shown_exactly_when_showLabel_and_showValue_are_set()
	{
		var both = Render(new { label = "Vol", showLabel = true, showValue = true, action = Action() });
		var neither = Render(new { label = "Vol", showLabel = false, showValue = false, action = Action() });

		Assert.Multiple(() =>
		{
			Assert.That(both.FindById("slider.header.lead.label"), Is.Not.Null);
			Assert.That(both.FindById("slider.header.value"), Is.Not.Null);
			Assert.That(neither.FindById("slider.header"),
				Is.Null,
				"neither an icon nor a label nor a value leaves nothing for the header to hold");
		});
	}

	[Test]
	public void An_unbound_slider_is_a_065_level_with_no_events_property()
	{
		var host = Render(new { });

		var track = host.ById("slider.track");

		Assert.Multiple(() =>
		{
			Assert.That(track.Number("level"), Is.EqualTo(0.65).Within(1e-9));
			Assert.That(track.HasProperty("events"), Is.False);
		});
	}

	[Test]
	public void A_bound_slider_declares_both_event_names()
	{
		var host = Render(new { action = Action() }, _boundEvents);

		var events = host.ById("slider.track").Property("events");

		Assert.That(events, Is.Not.Null);

		var names = events!.Value.EnumerateArray().Select(e => e.GetString()).ToList();

		Assert.That(names, Is.EquivalentTo(_bothEventNames));
	}

	[Test]
	public void A_configured_hex_colour_reaches_levelColor_while_a_theme_token_and_garbage_do_not()
	{
		var configured = Render(new { color = "#123ABC" });
		var themed = Render(new { color = "var(--color-accent)" });
		var garbage = Render(new { color = "not-a-colour" });

		Assert.Multiple(() =>
		{
			Assert.That(configured.ById("slider.track").Text("levelColor"), Is.EqualTo("#123abc"));
			Assert.That(themed.ById("slider.track").HasProperty("levelColor"),
				Is.False,
				"the retired client's CSS-variable token means unset, not a literal colour");
			Assert.That(garbage.ById("slider.track").HasProperty("levelColor"), Is.False);
		});
	}

	[Test]
	public void A_named_css_colour_normalises_to_its_hex_value()
	{
		var host = Render(new { color = "red" });

		Assert.That(host.ById("slider.track").Text("levelColor"), Is.EqualTo("#ff0000"));
	}

	[Test]
	public void Vertical_orientation_sets_direction_and_puts_the_value_last()
	{
		var host = Render(new
		{
			orientation = "vertical", label = "Vol", showLabel = true, showValue = true, action = Action(),
		});

		var slider = host.ById("slider");
		var track = host.ById("slider.track");

		Assert.Multiple(() =>
		{
			Assert.That(track.Text("direction"), Is.EqualTo("vertical"));
			Assert.That(slider.Children.Select(c => c.Id), Is.EqualTo(_verticalChildIds));
			Assert.That(host.FindById("slider.header.value"),
				Is.Null,
				"in vertical orientation the value sits below the track, not beside the label");
		});
	}

	[Test]
	public void The_track_carries_a_range_bar_fallback_with_both_colours_set()
	{
		var host = Render(new { color = "#112233" });
		var fallback = host.ById("slider.track").Fallback;

		Assert.That(fallback, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(fallback!.Type, Is.EqualTo(UiComponents.RangeBar));
			Assert.That(fallback.Text("startColor"), Is.EqualTo("#112233"));
			Assert.That(fallback.Text("endColor"), Is.EqualTo("#112233"));
		});
	}

	[Test]
	public void The_range_bar_fallback_uses_the_brand_accent_when_no_colour_is_configured()
	{
		var host = Render(new { });
		var fallback = host.ById("slider.track").Fallback!;

		Assert.Multiple(() =>
		{
			Assert.That(fallback.Text("startColor"), Is.EqualTo("#2196f3"));
			Assert.That(fallback.Text("endColor"), Is.EqualTo("#2196f3"));
		});
	}

	[Test]
	public void A_variable_only_slider_computes_its_level_from_the_readout_like_an_action_bound_one()
	{
		var config = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(new { valueVariable = "vol" }));
		var state = new UiState<SliderWidgetReadout>(new SliderWidgetReadout(true, 0, 100, 1, 75));
		var element = SliderWidgetView.Build(config, state, icon: null, _boundEvents);
		var host = UiTestHost.Render(element,
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });

		var track = host.ById("slider.track");

		Assert.Multiple(() =>
		{
			Assert.That(track.Number("level"), Is.EqualTo(0.75).Within(1e-9));
			Assert.That(track.HasProperty("events"),
				Is.True,
				"a variable-only slider is bound, so the unbound-preview level must not apply");
		});
	}

	[TestCase(100, 0, TestName = "MinAboveMax")]
	[TestCase(50, 50, TestName = "MinEqualsMax")]
	public void ComputeLevel_never_divides_by_zero_on_a_degenerate_configured_range(double min, double max)
	{
		var level = SliderWidgetView.ComputeLevel(new SliderWidgetReadout(true, min, max, 0, 75));

		Assert.That(double.IsFinite(level), Is.True);
		Assert.That(level, Is.EqualTo(0));
	}

	[Test]
	public void ComputeStepFraction_is_null_when_the_configured_step_is_zero()
	{
		var fraction = SliderWidgetView.ComputeStepFraction(new SliderWidgetReadout(true, 0, 100, 0, 50));

		Assert.That(fraction, Is.Null);
	}

	/// <summary>ADR 0081 removed the Slider's action binding, but stored widget data still carries the
	/// block. Parsing has to read the variable binding out of such a document and ignore the leftover
	/// rather than fail on it.</summary>
	[Test]
	public void Parse_reads_the_variable_binding_and_its_range_past_a_leftover_action_block()
	{
		var config = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(new
		{
			valueVariable = "vol", min = -60, max = 0, step = 0.5, action = Action(),
		}));

		Assert.Multiple(() =>
		{
			Assert.That(config.ValueVariable, Is.EqualTo("vol"));
			Assert.That(config.Min, Is.EqualTo(-60));
			Assert.That(config.Max, Is.EqualTo(0));
			Assert.That(config.Step, Is.EqualTo(0.5));
		});
	}

	[Test]
	public void Min_max_and_step_default_so_a_pre_existing_action_only_widget_still_parses_unchanged()
	{
		// Widget data saved before this feature existed - an action block and no variable key at all.
		var config = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(new { action = Action() }));

		Assert.Multiple(() =>
		{
			Assert.That(config.ValueVariable, Is.Null);
			Assert.That(config.Min, Is.EqualTo(0));
			Assert.That(config.Max, Is.EqualTo(100));
			Assert.That(config.Step, Is.EqualTo(1));
		});
	}

	[Test]
	public void The_stored_json_validates_against_the_slider_schema_with_and_without_the_new_keys()
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.Slider, out var schema), Is.True);

		var withVariable = """
						   {"valueVariable":"vol","min":-60,"max":0,"step":0.5,"action":{"integrationId":"i","actionId":"a"}}
						   """;
		var legacy = """{"action":{"integrationId":"i","actionId":"a"}}""";

		using var withVariableDocument = JsonDocument.Parse(withVariable);
		using var legacyDocument = JsonDocument.Parse(legacy);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, withVariableDocument.RootElement), Is.Empty);
			Assert.That(WidgetDataSchema.Validate(schema!, legacyDocument.RootElement),
				Is.Empty,
				"a client that does not know the new keys must still validate the widget it saved");
		});
	}

	private static object Action() => new { integrationId = "integration", actionId = "action" };

	private static UiTestHost Render(object data, IReadOnlyList<UiEventHandler>? events = null)
	{
		var config = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(data));
		var state = new UiState<SliderWidgetReadout>(new SliderWidgetReadout(true, 0, 100, 0, 42));
		var element = SliderWidgetView.Build(config, state, icon: null, events ?? []);

		return UiTestHost.Render(element,
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared });
	}
}
