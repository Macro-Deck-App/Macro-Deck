using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.HistoryGraph;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The History Graph widget's <c>widget-config</c> tree (issue #837): every key the shipped schema declares
/// for it, the <c>subtitle</c> dependency on <c>showSubtitle</c>, a preset writing its five fields
/// together, and the provider's decline of a foreign widget type. <c>historyLength</c> has no control and is
/// asserted absent from every key this tree ever touches.
/// </summary>
[TestFixture]
public class HistoryGraphWidgetConfigTests
{
	private static readonly object _stored = new
	{
		valueVariable = "custom_metric",
		title = "Custom",
		showSubtitle = true,
		subtitle = "custom caption",
		maxValue = 50,
		accentColor = "#ff0000",
		border = new { style = "static", color = "#ff0000" },
	};

	[Test]
	public void Editing_every_control_still_validates_against_the_history_graph_schema()
	{
		var host = Render(_stored);

		host.ById("valueVariable").Change("system_ram_usage_percent");
		host.ById("title").Change("RAM");
		host.ById("subtitle").Change("{{ vars.system_ram_name }}");
		host.ById("maxValue").Change(100);
		host.ById("minValue").Change(-100);
		host.ById("accentColor").Change("#00ff00");
		host.ById("border.style").Change("comet");
		host.ById("border.color").Change("#0000ff");

		var composed = Compose(host);
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.HistoryGraph, out var schema), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
			Assert.That(composed.GetProperty("valueVariable").GetString(), Is.EqualTo("system_ram_usage_percent"));
			Assert.That(composed.GetProperty("title").GetString(), Is.EqualTo("RAM"));
			Assert.That(composed.GetProperty("maxValue").GetDouble(), Is.EqualTo(100));
			Assert.That(composed.GetProperty("minValue").GetDouble(), Is.EqualTo(-100));
			Assert.That(composed.GetProperty("accentColor").GetString(), Is.EqualTo("#00ff00"));
		});
	}

	[Test]
	public void Subtitle_is_visible_exactly_while_showSubtitle_is_set()
	{
		var host = Render(new { showSubtitle = true });

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "subtitle"), Is.True);

		host.ById("showSubtitle").Change(false);

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "subtitle"), Is.False);
	}

	[Test]
	public void The_subtitle_is_a_freetext_field_that_accepts_variables_of_every_type()
	{
		var subtitle = Render(new { showSubtitle = true }).ById("subtitle");

		Assert.Multiple(() =>
		{
			Assert.That(subtitle.Type, Is.EqualTo(UiConfigPrimitives.String));
			Assert.That(subtitle.HasProperty(UiConfigProperties.LiteralOnly),
				Is.False,
				"a literal-only field renders a plain box with no variable helper");
			Assert.That(subtitle.HasProperty(UiConfigProperties.VariableTypes),
				Is.False,
				"the field the issue asks for filters no variable type out");
		});
	}

	[Test]
	public void A_profile_that_predates_the_freetext_subtitle_seeds_the_field_with_its_variable()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Render(new { showSubtitle = true, subtitleVariable = "system_cpu_name" })
					.ById("subtitle").Text(UiConfigProperties.Value),
				Is.EqualTo("{{ vars.system_cpu_name }}"));
			Assert.That(Render(new { showSubtitle = true, subtitleVariable = "system_cpu_name", subtitle = "" })
					.ById("subtitle").Text(UiConfigProperties.Value),
				Is.Empty,
				"a subtitle the user cleared must not be re-seeded from the older key on the next visit");
			Assert.That(Render(new { showSubtitle = true, subtitleVariable = "system_cpu_name", subtitle = "Mine" })
					.ById("subtitle").Text(UiConfigProperties.Value),
				Is.EqualTo("Mine"),
				"the form shows what the deck shows, so a later save cannot overwrite it");
		});
	}

	[Test]
	public void The_default_data_of_a_new_history_graph_names_its_subtitle_through_the_field()
	{
		var defaultData = BuiltInWidgetTypes.All(new HashSet<string>(StringComparer.Ordinal))
				.Single(type => type.Id == WidgetTypeIds.HistoryGraph)
				.DefaultData ??
			"{}";
		var host = Render(JsonSerializer.Deserialize<JsonElement>(defaultData));

		Assert.That(host.ById("subtitle").Text(UiConfigProperties.Value),
			Is.EqualTo("{{ vars.system_cpu_name }}"));
	}

	[Test]
	public void The_cpu_preset_writes_all_five_of_its_fields_together()
	{
		var host = Render(new { valueVariable = "something_else", title = "Something", minValue = -100 });

		// A button is chrome, not an input, so its id is the full structural path - through the presets
		// row it sits in - rather than a bare field name, unlike every other node this test touches.
		host.ById("root.properties.presets.presetCpu").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("valueVariable").Text(UiConfigProperties.Value),
				Is.EqualTo("system_cpu_usage_percent"));
			Assert.That(host.ById("title").Text(UiConfigProperties.Value), Is.EqualTo("CPU Load"));
			Assert.That(host.ById("subtitle").Text(UiConfigProperties.Value),
				Is.EqualTo("{{ vars.system_cpu_name }}"));
			Assert.That(host.ById("maxValue").Number(UiConfigProperties.Value), Is.EqualTo(100));
			Assert.That(host.ById("minValue").Number(UiConfigProperties.Value),
				Is.EqualTo(0),
				"a preset describes a whole metric, so it must not leave an unrelated floor behind");
		});
	}

	[Test]
	public void A_negative_minimum_survives_the_tree_unchanged()
	{
		var host = Render(new { valueVariable = "custom_metric", minValue = -100 });

		Assert.That(host.ById("minValue").Number(UiConfigProperties.Value),
			Is.EqualTo(-100),
			"clamping the floor the way the ceiling is clamped would erase the only values it exists for");
	}

	[Test]
	public void The_minimum_field_declares_no_lower_bound_of_its_own()
	{
		var host = Render(_stored);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("maxValue").Number(UiConfigProperties.Min), Is.EqualTo(0));
			Assert.That(host.ById("minValue").Number(UiConfigProperties.Min),
				Is.Null,
				"a bound of zero would reject the negative floors the field exists to accept");
		});
	}

	[Test]
	public void HistoryLength_has_no_node_and_is_never_among_the_keys_the_tree_configures()
	{
		var host = Render(_stored);

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("historyLength"), Is.Null);
			Assert.That(host.FindById("subtitleVariable"), Is.Null);
		});
	}

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		var provider = new HistoryGraphWidgetUiProvider(new MacroDeckHost.Application.Variables.VariableRegistry(),
			new NullVariableHistory(),
			new MacroDeckHost.Application.Variables.VariableChangeNotifier(),
			new PassThroughSampleText());

		var surface = ConfigSurface(WidgetTypeIds.Clock, "{}");

		var session = await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	// ---- resetting a customised colour (issue #896) ------------------------------------------------------

	[Test]
	public void The_accent_and_border_colours_offer_a_reset_that_clears_them()
	{
		var host = Render(_stored);

		var accent = host.ById("accentColor");
		var borderColor = host.ById("border.color");

		Assert.Multiple(() =>
		{
			Assert.That(accent.Flag(UiConfigProperties.SupportsReset), Is.True);
			Assert.That(accent.Text(UiConfigProperties.DefaultValue), Is.EqualTo(string.Empty));
			Assert.That(borderColor.Flag(UiConfigProperties.SupportsReset), Is.True);
			Assert.That(borderColor.Text(UiConfigProperties.DefaultValue), Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public void A_reset_colour_is_stored_cleared_and_still_validates_against_the_schema()
	{
		var host = Render(_stored);

		host.ById("accentColor").Change(string.Empty);
		host.ById("border.color").Change(string.Empty);

		var composed = Compose(host);
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.HistoryGraph, out var schema), Is.True);

		Assert.Multiple(() =>
		{
			// Cleared rather than repainted with a literal: the accent the reader sees is whatever the
			// theme currently is, so a reset that wrote today's hex would freeze the chart at it.
			Assert.That(composed.GetProperty("accentColor").GetString(), Is.Empty);
			Assert.That(composed.GetProperty("border").GetProperty("color").GetString(), Is.Empty);
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
		});
	}

	private static UiTestHost Render(object data)
		=> UiTestHost.Render(HistoryGraphWidgetConfigView.Build(JsonSerializer.SerializeToElement(data)));

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
			["valueVariable"] = host.ById("valueVariable").Text(UiConfigProperties.Value),
			["title"] = host.ById("title").Text(UiConfigProperties.Value),
			["showSubtitle"] = host.ById("showSubtitle").Flag(UiConfigProperties.Value),
			["subtitle"] = host.ById("subtitle").Text(UiConfigProperties.Value),
			["maxValue"] = host.ById("maxValue").Number(UiConfigProperties.Value),
			["minValue"] = host.ById("minValue").Number(UiConfigProperties.Value),
			["accentColor"] = host.ById("accentColor").Text(UiConfigProperties.Value),
			["border"] = border,
		};

		return JsonSerializer.SerializeToElement(data);
	}

	private sealed class NullVariableHistory : MacroDeckHost.Application.Variables.IVariableHistory
	{
		public MacroDeckHost.Application.Variables.IVariableHistoryWindow Open(string variableName, int capacity, string? scopeRefId = null)
			=> EmptyVariableHistoryWindow.Instance;
	}

	private sealed class PassThroughSampleText : MacroDeckHost.Widgets.Preview.IWidgetSampleTextResolver
	{
		public ValueTask<string> ResolveAsync(MacroDeck.Localization.LocalizedString value)
			=> ValueTask.FromResult(value.ToString() ?? string.Empty);
	}
}
