using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Widgets;

namespace MacroDeckHost.Widgets.Configuration;

/// <summary>
/// What every built-in widget's <c>config</c> surface branch needs from <see cref="UiSurface.Attributes" />:
/// whether the surface is genuinely this widget's <c>widget-config</c> entry point, and the stored data it
/// carries. Shared so a widget provider's decline check and its data read agree, rather than five copies of the
/// same two attribute lookups drifting apart.
/// </summary>
internal static class WidgetConfigSurfaces
{
	/// <summary>Whether <paramref name="surface" /> is a <c>widget-config</c> entry point for
	/// <paramref name="widgetType" />. A provider declines - returns <c>null</c>, not an error - for anything
	/// else, including a <c>widget-config</c> surface naming a different widget type.</summary>
	public static bool IsFor(UiSurface surface, string widgetType)
	{
		return UiWidgetSurface.IsConfigurationFor(surface, widgetType);
	}

	/// <summary>The widget's stored configuration, seeded from <see cref="UiConfigSurfaceAttributes.WidgetData" />.
	/// Absent or non-object data reads as an empty object, exactly as the entry point that opened this session
	/// already promises.</summary>
	public static JsonElement Data(UiSurface surface)
	{
		return UiWidgetSurface.Data(surface);
	}

	/// <summary>The widget being configured, so a provider can resolve something about the live instance -
	/// its currently rendered state, say - rather than only the draft data travelling on the surface. Null
	/// only when the attribute is missing or malformed, which <see cref="IsFor" /> already rules out for a
	/// genuine <c>widget-config</c> entry point.</summary>
	public static Guid? WidgetId(UiSurface surface)
	{
		return UiWidgetSurface.WidgetId(surface);
	}

	/// <summary>
	/// The widget's width divided by its height, for a control that previews the widget and has to frame it
	/// the way the deck will. A missing or non-positive dimension reads as one cell, which is what a widget
	/// being created has before it is placed.
	/// </summary>
	public static double AspectRatio(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		var width = ReadPositiveInt(surface, UiConfigSurfaceAttributes.WidgetWidth);
		var height = ReadPositiveInt(surface, UiConfigSurfaceAttributes.WidgetHeight);

		return (double)width / height;
	}

	private static int ReadPositiveInt(UiSurface surface, string attribute)
	{
		return surface.Attributes.TryGetValue(attribute, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var number)
			&& number > 0
				? number
				: 1;
	}
}
