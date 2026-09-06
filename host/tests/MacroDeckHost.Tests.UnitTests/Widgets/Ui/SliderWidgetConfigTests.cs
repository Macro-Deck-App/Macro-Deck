using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.Slider;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The Slider widget's <c>widget-config</c> tree (issue #837): every key the shipped schema declares for it,
/// the per-field <c>min</c>/<c>max</c>/<c>step</c> fallback the bound variable's own declared bounds
/// override, and the provider's decline of a foreign widget type. Unlike the other four widgets, Slider has
/// no <c>flows</c> region at all.
/// </summary>
[TestFixture]
public class SliderWidgetConfigTests
{
	private const string _boundVariable = "vol";

	private static readonly object _stored = new
	{
		label = "Volume",
		showLabel = true,
		showValue = true,
		orientation = "horizontal",
		color = "#111111",
		labelColor = "#222222",
		backgroundColor = "#333333",
		border = new { style = "static", color = "#ff0000" },
		valueVariable = _boundVariable,
		min = 0,
		max = 100,
		step = 1,
	};

	[Test]
	public void Editing_every_control_still_validates_against_the_slider_schema()
	{
		var host = Render(_stored, Registry());

		host.ById("label").Change("Master");
		host.ById("showLabel").Change(false);
		host.ById("showValue").Change(false);
		host.ById("orientation").Change("vertical");
		host.ById("color").Change("#444444");
		host.ById("labelColor").Change("#555555");
		host.ById("backgroundColor").Change("#666666");
		host.ById("border.style").Change("comet");
		host.ById("border.color").Change("#00ff00");
		host.ById("min").Change(-60);
		host.ById("max").Change(0);
		host.ById("step").Change(0.5);

		var composed = Compose(host);
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.Slider, out var schema), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
			Assert.That(composed.GetProperty("label").GetString(), Is.EqualTo("Master"));
			Assert.That(composed.GetProperty("orientation").GetString(), Is.EqualTo("vertical"));
			Assert.That(composed.GetProperty("min").GetDouble(), Is.EqualTo(-60));
			Assert.That(composed.GetProperty("border").GetProperty("style").GetString(), Is.EqualTo("comet"));
		});
	}

	[Test]
	public void There_is_no_flows_region_at_all()
	{
		var host = Render(_stored, Registry());

		Assert.That(host.FindById("flows"), Is.Null);
	}

	[Test]
	public void A_bound_variable_declaring_its_own_minimum_hides_min_while_max_and_step_stay_visible()
	{
		var registry = new VariableRegistry();
		registry.Upsert(Variable(min: -10, max: null, step: null));

		var host = Render(new { valueVariable = _boundVariable }, registry);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "min"), Is.False);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "max"), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "step"), Is.True);
		});
	}

	[Test]
	public void All_three_range_fields_are_hidden_while_nothing_is_bound()
	{
		var host = Render(new { }, new VariableRegistry());

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "min"), Is.False);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "max"), Is.False);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "step"), Is.False);
		});
	}

	[Test]
	public void All_three_range_fields_are_visible_for_a_variable_declaring_no_bounds_of_its_own()
	{
		var host = Render(new { valueVariable = _boundVariable }, Registry());

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "min"), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "max"), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "step"), Is.True);
		});
	}

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		var provider = new SliderWidgetUiProvider(new FakeWidgetIconResources(),
			new FakeHostLockState(),
			new PassThroughSampleText(),
			TimeProvider.System,
			new VariableRegistry(),
			new VariableChangeNotifier(),
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

		var surface = ConfigSurface(WidgetTypeIds.Clock, "{}");

		var session = await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	// ---- resetting a customised colour (issue #896) ------------------------------------------------------

	[Test]
	public void Every_slider_colour_offers_a_reset_that_clears_it()
	{
		var host = Render(_stored, Registry());

		foreach (var id in new[] { "color", "labelColor", "backgroundColor", "border.color" })
		{
			var node = host.ById(id);

			Assert.Multiple(() =>
			{
				Assert.That(node.Flag(UiConfigProperties.SupportsReset), Is.True, id);
				Assert.That(node.Text(UiConfigProperties.DefaultValue), Is.EqualTo(string.Empty), id);
			});
		}
	}

	private static VariableRegistry Registry()
	{
		var registry = new VariableRegistry();
		registry.Upsert(Variable(min: null, max: null, step: null));

		return registry;
	}

	private static VariableEntity Variable(double? min, double? max, double? step)
		=> new()
		{
			Id = Guid.NewGuid(),
			Name = _boundVariable,
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "50",
			Min = min,
			Max = max,
			Step = step,
			UpdatedAt = DateTime.UtcNow,
		};

	private static UiTestHost Render(object data, VariableRegistry variables)
		=> UiTestHost.Render(SliderWidgetConfigView.Build(JsonSerializer.SerializeToElement(data), variables));

	private static UiSurface ConfigSurface(string widgetType, string widgetData)
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.Deserialize<JsonElement>(widgetData),
			},
		};

	private static JsonElement Compose(UiTestHost host)
	{
		var border = new Dictionary<string, object?>
		{
			["style"] = host.ById("border.style").Text(UiConfigProperties.Value),
			["color"] = host.ById("border.color").Text(UiConfigProperties.Value),
		};

		var data = new Dictionary<string, object?>
		{
			["label"] = host.ById("label").Text(UiConfigProperties.Value),
			["showLabel"] = host.ById("showLabel").Flag(UiConfigProperties.Value),
			["showValue"] = host.ById("showValue").Flag(UiConfigProperties.Value),
			["orientation"] = host.ById("orientation").Text(UiConfigProperties.Value),
			["color"] = host.ById("color").Text(UiConfigProperties.Value),
			["labelColor"] = host.ById("labelColor").Text(UiConfigProperties.Value),
			["backgroundColor"] = host.ById("backgroundColor").Text(UiConfigProperties.Value),
			["border"] = border,
			["valueVariable"] = host.ById("valueVariable").Text(UiConfigProperties.Value),
			["min"] = host.ById("min").Number(UiConfigProperties.Value),
			["max"] = host.ById("max").Number(UiConfigProperties.Value),
			["step"] = host.ById("step").Number(UiConfigProperties.Value),
		};

		return JsonSerializer.SerializeToElement(data);
	}

	private sealed class FakeWidgetIconResources : IWidgetIconResources
	{
		public Task<MacroDeck.Ui.Model.Resources.UiResource?> ResolveAsync(WidgetIconReference? reference,
			CancellationToken cancellationToken)
			=> Task.FromResult<MacroDeck.Ui.Model.Resources.UiResource?>(null);

		public void Evict(Guid iconId)
		{
		}
	}

	private sealed class PassThroughSampleText : IWidgetSampleTextResolver
	{
		public ValueTask<string> ResolveAsync(MacroDeck.Localization.LocalizedString value)
			=> ValueTask.FromResult(value.ToString() ?? string.Empty);
	}
}
