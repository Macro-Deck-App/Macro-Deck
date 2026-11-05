namespace MacroDeck.Sdk.Profiles;

/// <summary>
/// A UI interaction routed to an integration for one of its virtual widgets. <see cref="TriggerType"/> is a
/// widget trigger name, the same on every route: "onShortPress", "onLongPress" or "onDoublePress" for a
/// press, or another widget trigger name such as "onTouchStart".
/// </summary>
public sealed record WidgetInteraction(string TriggerType);
