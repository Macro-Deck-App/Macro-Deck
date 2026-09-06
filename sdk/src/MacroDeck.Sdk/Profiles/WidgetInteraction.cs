namespace MacroDeck.Sdk.Profiles;

/// <summary>
/// A UI interaction routed to an integration for one of its virtual widgets. <see cref="TriggerType"/>
/// mirrors the action-button trigger names (e.g. "press", "release").
/// </summary>
public sealed record WidgetInteraction(string TriggerType);
