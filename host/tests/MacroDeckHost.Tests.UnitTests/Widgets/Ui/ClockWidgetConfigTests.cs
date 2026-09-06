using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Clock;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The Clock widget's <c>widget-config</c> tree (issue #837): every key the shipped schema declares for it,
/// the <c>label</c> field's dependency on <c>showLabel</c>, and the provider's decline of a surface naming a
/// different widget type.
/// </summary>
[TestFixture]
public class ClockWidgetConfigTests
{
	private static readonly object _stored = new
	{
		style = "digital",
		timeZone = "America/New_York",
		showLabel = true,
		label = "Office",
		showSeconds = true,
		showDate = true,
		border = new { style = "static", color = "#ff0000" },
		flows = "[{\"trigger\":\"onShortPress\"}]",
	};

	[Test]
	public void Editing_every_control_still_validates_against_the_clock_schema()
	{
		var host = Render(_stored);

		host.ById("style").Change("analog");
		host.ById("timeZone").Change("Europe/Berlin");
		host.ById("showSeconds").Change(false);
		host.ById("showDate").Change(false);
		host.ById("border.style").Change("comet");
		host.ById("border.color").Change("#00ff00");

		var composed = Compose(host);
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.Clock, out var schema), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
			Assert.That(composed.GetProperty("style").GetString(), Is.EqualTo("analog"));
			Assert.That(composed.GetProperty("timeZone").GetString(), Is.EqualTo("Europe/Berlin"));
			Assert.That(composed.GetProperty("showSeconds").GetBoolean(), Is.False);
			Assert.That(composed.GetProperty("showDate").GetBoolean(), Is.False);
			Assert.That(composed.GetProperty("border").GetProperty("style").GetString(), Is.EqualTo("comet"));
			Assert.That(composed.GetProperty("border").GetProperty("color").GetString(), Is.EqualTo("#00ff00"));
		});
	}

	[Test]
	public void The_label_field_is_visible_exactly_while_showLabel_is_set()
	{
		var host = Render(new { showLabel = false });

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "label"), Is.False);

		host.ById("showLabel").Change(true);

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "label"), Is.True);

		host.ById("showLabel").Change(false);

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "label"), Is.False);
	}

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		var provider = new ClockWidgetUiProvider();
		var surface = ConfigSurface(WidgetTypeIds.Weather, "{}");

		var session = await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	private static UiTestHost Render(object data)
		=> UiTestHost.Render(ClockWidgetConfigView.Build(JsonSerializer.SerializeToElement(data)));

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

	/// <summary>Rebuilds the widget data object from the tree's current values, exactly as the client's
	/// structural composition (<c>config-draft.util.ts</c>) would for a widget with only these keys -
	/// enough to prove the tree's controls can only ever produce schema-valid data.</summary>
	private static JsonElement Compose(UiTestHost host)
	{
		var border = new Dictionary<string, object?>
		{
			["style"] = host.ById("border.style").Text(UiConfigProperties.Value),
			["color"] = host.ById("border.color").Text(UiConfigProperties.Value),
		};

		var data = new Dictionary<string, object?>
		{
			["style"] = host.ById("style").Text(UiConfigProperties.Value),
			["timeZone"] = host.ById("timeZone").Text(UiConfigProperties.Value),
			["showLabel"] = host.ById("showLabel").Flag(UiConfigProperties.Value),
			["label"] = host.ById("label").Text(UiConfigProperties.Value),
			["showSeconds"] = host.ById("showSeconds").Flag(UiConfigProperties.Value),
			["showDate"] = host.ById("showDate").Flag(UiConfigProperties.Value),
			["border"] = border,
		};

		return JsonSerializer.SerializeToElement(data);
	}

	/// <summary>
	/// Every key the Clock's own schema declares either has a control or is deliberately not one the
	/// editor offers, so a key added to the widget cannot quietly end up with no way to set it. That is
	/// not hypothetical: #860 added seven keys to this schema while this configuration was being written,
	/// and without this the tree would have silently shipped without them.
	/// </summary>
	[Test]
	public void Every_key_the_clock_schema_declares_has_a_control()
	{
		var schema
			= new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator())).All()[WidgetTypeIds.Clock];
		var declared = schema
			.GetProperty("properties")
			.EnumerateObject()
			.Select(property => property.Name)
			.ToList();

		// timeSource says whether the time comes from this device or from the host's own clock. No editor
		// has ever offered it - it is chosen for the widget rather than by the person configuring one -
		// and a stored value survives editing untouched, because the draft only ever rewrites keys the
		// tree names. A key added here has to be a deliberate decision, which is the point of the list.
		var notOffered = new[] { "timeSource" };

		var host = Render(_stored);
		var configured = new HashSet<string>(StringComparer.Ordinal);

		foreach (var id in NodeIds(host.Tree.Root))
		{
			// A nested input is addressed as "border.style"; the key it configures is the root of that.
			var dot = id.IndexOf('.', StringComparison.Ordinal);
			configured.Add(dot < 0 ? id : id[..dot]);
		}

		Assert.That(declared.Where(key => !configured.Contains(key) && !notOffered.Contains(key)),
			Is.Empty,
			"a key the schema declares that no node configures cannot be set through the widget editor");
	}

	private static IEnumerable<string> NodeIds(MacroDeck.Ui.Model.Nodes.UiNode node)
	{
		yield return node.Id;

		foreach (var id in node.Children.SelectMany(NodeIds))
		{
			yield return id;
		}
	}
}
