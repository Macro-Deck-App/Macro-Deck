using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.Slider;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The Slider widget's <c>widget-config</c> tree (issue #837): every key the shipped schema declares for it,
/// the per-field <c>min</c>/<c>max</c>/<c>step</c> fallback the bound variable's own declared bounds
/// override, and the provider's decline of a foreign widget type.
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
	public void All_three_range_fields_are_visible_while_nothing_is_picked_since_slider_value_declares_no_range()
	{
		var host = Render(new { }, new VariableRegistry());

		Assert.Multiple(() =>
		{
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "min"), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "max"), Is.True);
			Assert.That(WidgetConfigTestSupport.IsVisible(host, "step"), Is.True);
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
	public void The_editor_offers_an_actions_list_limited_to_the_double_tap_trigger()
	{
		var host = Render(_stored, Registry());

		var triggers = host.SingleByType(UiConfigPrimitives.ActionsListEditor).Property("triggers");

		Assert.That(triggers?.EnumerateArray().Select(trigger => trigger.GetString()),
			Is.EqualTo(new[] { WidgetTriggerTypes.DoublePress }));
	}

	[Test]
	public void The_binding_sits_with_the_properties_and_the_editor_holds_only_the_actions()
	{
		var host = Render(_stored, Registry());

		static string? RegionOf(UiTestNode node)
		{
			for (var current = node.Parent; current is not null; current = current.Parent)
			{
				if (current.Type is UiConfigPrimitives.WidgetProperties or UiConfigPrimitives.WidgetEditor)
				{
					return current.Type;
				}
			}

			return null;
		}

		var editor = host.SingleByType(UiConfigPrimitives.WidgetEditor);

		Assert.Multiple(() =>
		{
			foreach (var id in new[] { "valueVariable", "min", "max", "step" })
			{
				Assert.That(RegionOf(host.ById(id)), Is.EqualTo(UiConfigPrimitives.WidgetProperties), id);
			}

			Assert.That(editor.Children.Select(child => child.Type),
				Is.EqualTo(new[] { UiConfigPrimitives.ActionsListEditor }));
		});
	}

	[Test]
	public void An_unbound_slider_still_offers_the_double_tap_actions()
	{
		var host = Render(new { label = "Volume" }, Registry());

		Assert.That(host.ByType(UiConfigPrimitives.ActionsListEditor), Has.Count.EqualTo(1));
	}

	[TestCase(UiSurfaceKinds.Widget, true)]
	[TestCase(UiSurfaceKinds.Preview, false)]
	public async Task A_stored_double_tap_flow_makes_only_the_deck_tile_declare_double_press(string kind, bool expected)
	{
		var data = JsonSerializer.SerializeToElement(new
		{
			valueVariable = _boundVariable,
			flows = """[{"triggerType":"onDoublePress","children":[{"type":"action"}]}]""",
		});
		var surface = new UiSurface
		{
			Kind = kind,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.Data] = data,
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
			},
		};

		var session = await Provider().CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);
		var track = Walk(session!.BuildTree().Root).Single(node => node.Type == UiComponents.Slider);
		await session.DisposeAsync();

		var declared = track.Properties.TryGetValue("events", out var events) &&
			events.EnumerateArray().Any(name => name.GetString() == UiComponentEvents.DoublePress);

		Assert.That(declared, Is.EqualTo(expected));
	}

	[TestCase("""[{"triggerType":"onDoublePress","children":[{"type":"action"}]}]""", true)]
	[TestCase("""[{"triggerType":"onDoublePress","children":[{"type":"action","disabled":true}]}]""", false)]
	[TestCase("""[{"triggerType":"onDoublePress","children":[]}]""", false)]
	[TestCase("""[{"triggerType":"onShortPress","children":[{"type":"action"}]}]""", false)]
	public void Only_a_double_tap_flow_with_something_to_run_counts(string flows, bool expected)
	{
		var data = JsonSerializer.SerializeToElement(new { flows });

		Assert.That(SliderWidgetData.Parse(data).HasDoublePressFlow, Is.EqualTo(expected));
	}

	private static IEnumerable<UiNode> Walk(UiNode node) => node.Children.SelectMany(Walk).Prepend(node);

	private static SliderWidgetUiProvider Provider()
		=> new(new FakeWidgetIconResources(),
			new FakeHostLockState(),
			new PassThroughSampleText(),
			TimeProvider.System,
			new VariableRegistry(),
			new VariableChangeNotifier(),
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new SliderWidgetSessionTests.RecordingTriggerService(),
			new StubFolderCache(),
			new SliderWidgetSessionTests.NullUiTransport());

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		var provider = Provider();

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

	internal sealed class FakeWidgetIconResources : IWidgetIconResources
	{
		public Task<MacroDeck.Ui.Model.Resources.UiResource?> ResolveAsync(WidgetIconReference? reference,
			CancellationToken cancellationToken)
			=> Task.FromResult<MacroDeck.Ui.Model.Resources.UiResource?>(null);

		public void Evict(Guid iconId)
		{
		}
	}

	internal sealed class PassThroughSampleText : IWidgetSampleTextResolver
	{
		public ValueTask<string> ResolveAsync(MacroDeck.Localization.LocalizedString value)
			=> ValueTask.FromResult(value.ToString() ?? string.Empty);
	}
}
