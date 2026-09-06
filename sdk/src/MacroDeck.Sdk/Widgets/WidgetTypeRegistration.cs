namespace MacroDeck.Sdk.Widgets;

/// <summary>The host's identity for a registered widget type.</summary>
/// <param name="WidgetTypeId">
/// The qualified id - <c>provider.id::widget-type-id</c> - a widget stores as its type. The host derives it;
/// a provider never constructs it itself.
/// </param>
/// <param name="ProviderId">The integration or plugin that owns the type.</param>
public sealed record WidgetTypeRegistration(string WidgetTypeId, string ProviderId);
