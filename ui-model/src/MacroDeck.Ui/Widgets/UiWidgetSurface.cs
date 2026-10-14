using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Widgets;

/// <summary>
/// Reads what a widget provider needs from a surface it is asked to serve: whether the surface is for its
/// widget type, the widget's stored configuration, the widget instance and whether a sample is requested.
/// Every reader is lenient: a missing or malformed attribute reads as absent rather than throwing, so a
/// provider declines a surface it does not understand by returning no session.
/// </summary>
public static class UiWidgetSurface
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement.Clone();

	/// <summary>
	/// Whether <paramref name="surface" /> renders a widget of <paramref name="widgetType" />: a
	/// <see cref="UiSurfaceKinds.Widget" /> or <see cref="UiSurfaceKinds.Preview" /> surface whose
	/// <see cref="UiWidgetSurfaceAttributes.WidgetType" /> is the qualified widget type id.
	/// </summary>
	public static bool IsFor(UiSurface surface, string widgetType)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentException.ThrowIfNullOrEmpty(widgetType);

		return surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview
			&& ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) == widgetType;
	}

	/// <summary>
	/// Whether <paramref name="surface" /> is the <see cref="UiConfigEntryPoints.WidgetConfig" /> entry point
	/// of a widget of <paramref name="widgetType" />.
	/// </summary>
	public static bool IsConfigurationFor(UiSurface surface, string widgetType)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentException.ThrowIfNullOrEmpty(widgetType);

		return surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig
			&& ReadString(surface, UiConfigSurfaceAttributes.WidgetType) == widgetType;
	}

	/// <summary>
	/// The widget's stored configuration: <see cref="UiWidgetSurfaceAttributes.Data" /> on a widget or preview
	/// surface, <see cref="UiConfigSurfaceAttributes.WidgetData" /> on a widget configuration surface. Absent
	/// or non-object data reads as an empty object.
	/// </summary>
	public static JsonElement Data(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		var key = surface.Kind == UiSurfaceKinds.Config
			? UiConfigSurfaceAttributes.WidgetData
			: UiWidgetSurfaceAttributes.Data;

		return surface.Attributes.TryGetValue(key, out var data) && data.ValueKind == JsonValueKind.Object
			? data
			: _emptyObject;
	}

	/// <summary>
	/// The stored widget the surface renders or configures, or <c>null</c> when it names none: a preview, or a
	/// widget that has not been saved yet.
	/// </summary>
	public static Guid? WidgetId(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		var key = surface.Kind == UiSurfaceKinds.Config
			? UiConfigSurfaceAttributes.WidgetId
			: UiWidgetSurfaceAttributes.WidgetId;

		return Guid.TryParse(ReadString(surface, key), out var widgetId) ? widgetId : null;
	}

	/// <summary>
	/// Whether the surface asks for a representative sample rather than live state, see
	/// <see cref="UiWidgetSurfaceAttributes.Sample" />. A provider answering it draws without reading anything
	/// live.
	/// </summary>
	public static bool IsSampleRequested(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Kind == UiSurfaceKinds.Preview
			&& surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var sample)
			&& sample.ValueKind == JsonValueKind.True;
	}

	/// <summary>
	/// The culture the host asked the widget to draw its own text in, from
	/// <see cref="UiWidgetSurfaceAttributes.Culture" />, or <c>null</c> when the surface states none.
	/// </summary>
	public static string? Culture(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return ReadString(surface, UiWidgetSurfaceAttributes.Culture) is { Length: > 0 } culture ? culture : null;
	}

	private static string? ReadString(UiSurface surface, string name)
	{
		return surface.Attributes.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}
}
