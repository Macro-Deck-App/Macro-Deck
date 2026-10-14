using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Widgets;

namespace MacroDeck.Ui.Tests.UnitTests.Widgets;

[TestFixture]
public class UiWidgetSurfaceTests
{
	private const string WidgetType = "com.example.plugin::counter";

	private static UiSurface Surface(string kind, params (string Key, object Value)[] attributes)
	{
		return new UiSurface
		{
			Kind = kind,
			SessionMode = UiSessionModes.Shared,
			Attributes = attributes.ToDictionary(attribute => attribute.Key,
				attribute => JsonSerializer.SerializeToElement(attribute.Value),
				StringComparer.Ordinal)
		};
	}

	[Test]
	public void A_widget_or_preview_surface_is_for_the_widget_type_it_names()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSurface.IsFor(Surface(UiSurfaceKinds.Widget,
						(UiWidgetSurfaceAttributes.WidgetType, WidgetType)),
					WidgetType),
				Is.True);
			Assert.That(UiWidgetSurface.IsFor(Surface(UiSurfaceKinds.Preview,
						(UiWidgetSurfaceAttributes.WidgetType, WidgetType)),
					WidgetType),
				Is.True);
			Assert.That(UiWidgetSurface.IsFor(Surface(UiSurfaceKinds.Widget,
						(UiWidgetSurfaceAttributes.WidgetType, "com.example.plugin::other")),
					WidgetType),
				Is.False);
			Assert.That(UiWidgetSurface.IsFor(Surface(UiSurfaceKinds.Dialog,
						(UiWidgetSurfaceAttributes.WidgetType, WidgetType)),
					WidgetType),
				Is.False);
		});
	}

	[Test]
	public void Only_the_widget_config_entry_point_of_the_widget_type_is_its_configuration()
	{
		var widgetConfig = Surface(UiSurfaceKinds.Config,
			(UiConfigSurfaceAttributes.EntryPoint, UiConfigEntryPoints.WidgetConfig),
			(UiConfigSurfaceAttributes.WidgetType, WidgetType));
		var actionConfig = Surface(UiSurfaceKinds.Config,
			(UiConfigSurfaceAttributes.EntryPoint, UiConfigEntryPoints.ActionConfig),
			(UiConfigSurfaceAttributes.WidgetType, WidgetType));

		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSurface.IsConfigurationFor(widgetConfig, WidgetType), Is.True);
			Assert.That(UiWidgetSurface.IsConfigurationFor(actionConfig, WidgetType), Is.False);
			Assert.That(UiWidgetSurface.IsFor(widgetConfig, WidgetType), Is.False);
		});
	}

	[Test]
	public void Data_and_widget_id_come_from_the_attributes_of_the_surface_kind()
	{
		var widgetId = Guid.NewGuid();
		var widget = Surface(UiSurfaceKinds.Widget,
			(UiWidgetSurfaceAttributes.WidgetId, widgetId.ToString()),
			(UiWidgetSurfaceAttributes.Data, new { count = 3 }));
		var config = Surface(UiSurfaceKinds.Config,
			(UiConfigSurfaceAttributes.WidgetId, widgetId.ToString()),
			(UiConfigSurfaceAttributes.WidgetData, new { count = 4 }));

		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSurface.WidgetId(widget), Is.EqualTo(widgetId));
			Assert.That(UiWidgetSurface.Data(widget).GetProperty("count").GetInt32(), Is.EqualTo(3));
			Assert.That(UiWidgetSurface.WidgetId(config), Is.EqualTo(widgetId));
			Assert.That(UiWidgetSurface.Data(config).GetProperty("count").GetInt32(), Is.EqualTo(4));
		});
	}

	[Test]
	public void Missing_or_malformed_attributes_read_as_absent()
	{
		var preview = Surface(UiSurfaceKinds.Preview,
			(UiWidgetSurfaceAttributes.WidgetId, "not-a-guid"),
			(UiWidgetSurfaceAttributes.Data, "not an object"));

		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSurface.WidgetId(preview), Is.Null);
			Assert.That(UiWidgetSurface.Data(preview).ValueKind, Is.EqualTo(JsonValueKind.Object));
			Assert.That(UiWidgetSurface.Data(preview).EnumerateObject(), Is.Empty);
		});
	}

	[Test]
	public void Only_a_preview_flagged_as_sample_asks_for_a_sample()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSurface.IsSampleRequested(Surface(UiSurfaceKinds.Preview,
					(UiWidgetSurfaceAttributes.Sample, true))),
				Is.True);
			Assert.That(UiWidgetSurface.IsSampleRequested(Surface(UiSurfaceKinds.Preview)), Is.False);
			Assert.That(UiWidgetSurface.IsSampleRequested(Surface(UiSurfaceKinds.Widget,
					(UiWidgetSurfaceAttributes.Sample, true))),
				Is.False);
		});
	}

	[Test]
	public void A_surface_carries_the_culture_the_host_asked_for_and_none_when_it_states_none()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSurface.Culture(Surface(UiSurfaceKinds.Preview,
					(UiWidgetSurfaceAttributes.Culture, "pt-BR"))),
				Is.EqualTo("pt-BR"));
			Assert.That(UiWidgetSurface.Culture(Surface(UiSurfaceKinds.Widget)), Is.Null);
			Assert.That(UiWidgetSurface.Culture(Surface(UiSurfaceKinds.Widget,
					(UiWidgetSurfaceAttributes.Culture, 42))),
				Is.Null);
			Assert.That(UiWidgetSurfaceAttributes.Culture, Is.EqualTo("culture"));
		});
	}

	[Test]
	public void The_safe_area_follows_the_corner_radius_the_surface_states()
	{
		var stated = Surface(UiSurfaceKinds.Widget, (UiWidgetSurfaceAttributes.CornerRadius, 60));

		Assert.Multiple(() =>
		{
			Assert.That(UiWidgetSafeArea.RadiusOf(stated), Is.EqualTo(60));
			Assert.That(UiWidgetSafeArea.RadiusOf(Surface(UiSurfaceKinds.Preview)),
				Is.EqualTo(UiWidgetSafeArea.DefaultCornerRadius));
			Assert.That(UiWidgetSafeArea.LengthFor(0).MaxOfCell!.Value * MacroDeck.Ui.Components.UiLength.Cell,
				Is.EqualTo(UiWidgetSafeArea.BreathingRoom).Within(0.001));
			Assert.That(UiWidgetSafeArea.LengthFor(120).MaxOfCell!.Value * MacroDeck.Ui.Components.UiLength.Cell,
				Is.EqualTo(120 * (1 - 1 / Math.Sqrt(2))).Within(0.001));
		});
	}
}
