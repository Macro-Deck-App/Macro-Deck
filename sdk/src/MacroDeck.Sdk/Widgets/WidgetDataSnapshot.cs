using System.Text.Json;

namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// The stored configuration of one widget of a type the caller registered, as
/// <see cref="IWidgetApi.GetWidgetDataAsync" /> reads it.
/// </summary>
/// <param name="WidgetId">The widget instance.</param>
/// <param name="WidgetTypeId">The qualified widget type id, <c>&lt;owner&gt;::&lt;local id&gt;</c>.</param>
/// <param name="Data">The widget's data as it is stored, always a JSON object. It is the saved configuration,
/// not a draft an editor is showing, and colour references are not resolved.</param>
public sealed record WidgetDataSnapshot(string WidgetId, string WidgetTypeId, JsonElement Data);
