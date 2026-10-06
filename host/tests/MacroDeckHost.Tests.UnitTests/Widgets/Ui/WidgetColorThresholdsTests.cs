using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets;
using MacroDeckHost.Widgets.HistoryGraph;
using MacroDeckHost.Widgets.Slider;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.HistoryGraphTestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class WidgetColorThresholdsTests
{
	private const string _green = "#34c759";
	private const string _yellow = "#ffcc00";
	private const string _orange = "#ff9500";
	private const string _red = "#ff3b30";

	private static readonly object _twoBands = new
	{
		bands = new object[] { new { id = "cool", color = "#0000ff" }, new { id = "hot", color = "#ff0000", from = 40 } },
	};

	private static readonly UiSurface _widgetSurface
		= new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	private static string? SliderTrackColor(object data, double value, double min = 0, double max = 100)
	{
		var config = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(data));
		var state = new UiState<SliderWidgetReadout>(new SliderWidgetReadout(true, min, max, 1, value));
		var host = UiTestHost.Render(SliderWidgetView.Build(config, state, null, []), _widgetSurface);

		return host.ById("slider.track").Text("levelColor");
	}

	[TestCase(10, _green)]
	[TestCase(50, _yellow)]
	[TestCase(80, _orange)]
	[TestCase(95, _red)]
	public void A_slider_with_thresholds_on_and_none_stored_paints_the_default_band_of_its_value(double value, string expected)
		=> Assert.That(SliderTrackColor(new { valueVariable = "vol", color = "#123456", thresholdsEnabled = true }, value),
			Is.EqualTo(expected));

	[Test]
	public void The_default_bands_follow_the_slider_range()
		=> Assert.That(SliderTrackColor(new { valueVariable = "vol", thresholdsEnabled = true }, -15, min: -60, max: 0),
			Is.EqualTo(_orange));

	[Test]
	public void A_slider_paints_its_stored_bands_and_ignores_its_accent_while_thresholds_are_on()
		=> Assert.Multiple(() =>
		{
			Assert.That(SliderTrackColor(new { valueVariable = "vol", color = "#123456", thresholdsEnabled = true, thresholds = _twoBands }, 39),
				Is.EqualTo("#0000ff"));
			Assert.That(SliderTrackColor(new { valueVariable = "vol", color = "#123456", thresholdsEnabled = true, thresholds = _twoBands }, 40),
				Is.EqualTo("#ff0000"));
		});

	[Test]
	public void A_slider_with_thresholds_off_keeps_its_accent_even_when_bands_are_stored()
		=> Assert.That(SliderTrackColor(new { valueVariable = "vol", color = "#123456", thresholds = _twoBands }, 90),
			Is.EqualTo("#123456"));

	[Test]
	public void Unreadable_stored_bands_fall_back_to_the_defaults()
		=> Assert.That(SliderTrackColor(new { valueVariable = "vol", thresholdsEnabled = true, thresholds = new { bands = "x" } }, 95),
			Is.EqualTo(_red));

	[Test]
	public void An_unbound_slider_sample_uses_the_band_of_its_preview_level_and_an_unfound_value_uses_the_accent()
	{
		var unbound = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(new { thresholdsEnabled = true }));
		var bound = SliderWidgetData.Parse(JsonSerializer.SerializeToElement(new { valueVariable = "vol", thresholdsEnabled = true }));

		Assert.Multiple(() =>
		{
			Assert.That(SliderWidgetView.TrackColor(unbound, SliderWidgetReadout.Empty, "#123456"), Is.EqualTo(_yellow));
			Assert.That(SliderWidgetView.TrackColor(bound, new SliderWidgetReadout(false, 0, 100, 1, 0), "#123456"),
				Is.EqualTo("#123456"));
		});
	}

	private static UiTestHost SliderConfig(object data, VariableRegistry? variables = null)
		=> UiTestHost.Render(SliderWidgetConfigView.Build(JsonSerializer.SerializeToElement(data), variables ?? new VariableRegistry()));

	[Test]
	public void An_untouched_slider_config_carries_a_null_thresholds_value_so_a_save_writes_no_key()
	{
		var host = SliderConfig(new { valueVariable = "vol", min = 0, max = 100 });
		var editor = host.ById(WidgetThresholds.ValueKey);

		Assert.Multiple(() =>
		{
			Assert.That(editor.Type, Is.EqualTo(UiConfigPrimitives.Thresholds));
			Assert.That(editor.Property(UiConfigProperties.Value)?.ValueKind ?? JsonValueKind.Null, Is.EqualTo(JsonValueKind.Null),
				"a null value is unset to the draft composer, which then writes no key");
			Assert.That(editor.HasProperty(UiConfigProperties.DefaultValue), Is.True);
			Assert.That(host.ById(WidgetThresholds.EnabledKey).Flag(UiConfigProperties.Value), Is.False);
		});
	}

	[Test]
	public void The_slider_editor_scale_and_defaults_follow_the_variable_declared_range_and_unit()
	{
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "temp",
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "20",
			Min = -20,
			Max = 60,
			Unit = "°C",
		});

		var editor = SliderConfig(new { valueVariable = "temp", min = 0, max = 100 }, registry).ById(WidgetThresholds.ValueKey);
		var parsed = UiThresholds.TryParse(editor.Property(UiConfigProperties.DefaultValue)!.Value, out var defaults);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(editor.Number(UiConfigProperties.Min), Is.EqualTo(-20));
			Assert.That(editor.Number(UiConfigProperties.Max), Is.EqualTo(60));
			Assert.That(editor.Property(UiConfigProperties.Unit)!.Value.ToString(), Does.Contain("°C"));
			Assert.That(defaults!.Bands.Select(band => band.From), Is.EqualTo(new double?[] { null, 20, 40, 52 }));
		});
	}

	[Test]
	public void Edited_slider_thresholds_validate_against_the_slider_schema_and_parse_back()
	{
		var host = SliderConfig(new { valueVariable = "vol" });

		Assert.That(host.ById(WidgetThresholds.EnabledKey).Change(true).IsAccepted, Is.True);
		Assert.That(host.ById(WidgetThresholds.ValueKey).Change(_twoBands).IsAccepted, Is.True);

		var composed = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
		{
			[WidgetThresholds.EnabledKey] = host.ById(WidgetThresholds.EnabledKey).Flag(UiConfigProperties.Value),
			[WidgetThresholds.ValueKey] = host.ById(WidgetThresholds.ValueKey).Property(UiConfigProperties.Value),
		});
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));
		Assert.That(provider.TryGet(WidgetTypeIds.Slider, out var schema), Is.True);

		var parsed = SliderWidgetData.Parse(composed);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
			Assert.That(parsed.ThresholdsEnabled, Is.True);
			Assert.That(parsed.Thresholds!.ColorAt(41), Is.EqualTo("#ff0000"));
		});
	}

	[Test]
	public void Both_schemas_reject_a_colourless_band_and_crossing_bands_are_refused_on_read()
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));
		var colourless = JsonSerializer.SerializeToElement(new { thresholds = new { bands = new object[] { new { id = "a" } } } });
		var crossing = JsonSerializer.SerializeToElement(new
		{
			valueVariable = "vol",
			thresholdsEnabled = true,
			thresholds = new
			{
				bands = new object[]
				{
					new { id = "a", color = "#000000" }, new { id = "b", color = "#ffffff", from = 50 },
					new { id = "c", color = "#ff0000", from = 20 },
				},
			},
		});

		Assert.Multiple(() =>
		{
			foreach (var type in new[] { WidgetTypeIds.Slider, WidgetTypeIds.HistoryGraph })
			{
				Assert.That(provider.TryGet(type, out var schema), Is.True);
				Assert.That(WidgetDataSchema.Validate(schema!, colourless), Is.Not.Empty, type);
				Assert.That(WidgetDataSchema.Validate(schema!, JsonSerializer.SerializeToElement(new { thresholds = _twoBands })),
					Is.Empty, type);
			}

			Assert.That(SliderWidgetData.Parse(crossing).Thresholds, Is.Null, "crossing bands read as none, so the defaults apply");
			Assert.That(HistoryGraphWidgetData.Parse(crossing).Thresholds, Is.Null);
		});
	}

	[TestCase("20", _green)]
	[TestCase("60", _yellow)]
	[TestCase("95", _red)]
	public void A_history_graph_with_thresholds_on_colours_its_chart_and_value_by_the_latest_value(string latest, string expected)
	{
		var data = new { valueVariable = Metric, maxValue = 100, accentColor = "#123456", thresholdsEnabled = true };
		var state = new UiState<HistoryGraphViewState>(Resolver(data, Registry(latest)).Resolve([10d, 20d]));
		var host = UiTestHost.Render(HistoryGraphWidgetView.Build(state, Config(data)), _widgetSurface);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("historyGraph.chart").Text(UiComponentProperties.Color), Is.EqualTo(expected));
			Assert.That(host.ById("historyGraph.valueLayer.valueRow.value").Text(UiComponentProperties.Color), Is.EqualTo(expected));
		});
	}

	[Test]
	public void A_history_graph_with_thresholds_off_keeps_its_accent_and_an_uncoloured_value()
	{
		var data = new { valueVariable = Metric, maxValue = 100, accentColor = "#123456", thresholds = _twoBands };
		var state = new UiState<HistoryGraphViewState>(Resolver(data, Registry("95")).Resolve([10d]));
		var host = UiTestHost.Render(HistoryGraphWidgetView.Build(state, Config(data)), _widgetSurface);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("historyGraph.chart").Text(UiComponentProperties.Color), Is.EqualTo("#123456"));
			Assert.That(host.ById("historyGraph.valueLayer.valueRow.value").HasProperty(UiComponentProperties.Color), Is.False);
		});
	}

	[Test]
	public void The_resolver_reports_no_colour_while_the_value_is_unavailable()
		=> Assert.That(Resolver(new { valueVariable = Metric, thresholdsEnabled = true }, Registry("n/a")).Resolve([]).Color,
			Is.Null);

	[Test]
	public void An_auto_scaled_graph_takes_its_threshold_scale_from_the_variable_and_otherwise_zero_to_a_hundred()
	{
		var declared = new VariableEntity
		{
			Name = "fps",
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "0",
			Min = 0,
			Max = 240,
		};

		Assert.Multiple(() =>
		{
			Assert.That(HistoryGraphViewStateResolver.ThresholdScale(Config(new { }), declared), Is.EqualTo((0d, 240d)));
			Assert.That(HistoryGraphViewStateResolver.ThresholdScale(Config(new { }), null), Is.EqualTo((0d, 100d)));
			Assert.That(HistoryGraphViewStateResolver.ThresholdScale(Config(new { minValue = -10, maxValue = 50 }), declared),
				Is.EqualTo((-10d, 50d)));
		});
	}

	[Test]
	public void An_untouched_history_graph_config_carries_a_null_thresholds_value()
	{
		var host = UiTestHost.Render(HistoryGraphWidgetConfigView.Build(JsonSerializer.SerializeToElement(new { valueVariable = Metric })));

		Assert.Multiple(() =>
		{
			Assert.That(host.ById(WidgetThresholds.ValueKey).Property(UiConfigProperties.Value)?.ValueKind ?? JsonValueKind.Null,
				Is.EqualTo(JsonValueKind.Null));
			Assert.That(host.ById(WidgetThresholds.ValueKey).Number(UiConfigProperties.Max), Is.EqualTo(100));
		});
	}

	[Test]
	public void Stored_graph_thresholds_survive_into_the_config_editor_unchanged()
	{
		var host = UiTestHost.Render(HistoryGraphWidgetConfigView.Build(
			JsonSerializer.SerializeToElement(new { valueVariable = Metric, thresholdsEnabled = true, thresholds = _twoBands })));

		Assert.That(JsonElement.DeepEquals(host.ById(WidgetThresholds.ValueKey).Property(UiConfigProperties.Value)!.Value,
			UiCanonicalJson.ToElement(JsonSerializer.SerializeToElement(_twoBands))), Is.True);
	}
}
