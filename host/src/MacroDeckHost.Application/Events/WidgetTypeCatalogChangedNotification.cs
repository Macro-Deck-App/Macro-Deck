using Mediator;

namespace MacroDeckHost.Application.Events;

/// <summary>A widget type was registered, unregistered, or an owner's types were all unregistered from
/// <c>IWidgetTypeRegistry</c> (issue #843). Marker only: subscribers re-derive whatever they need from the
/// registry themselves rather than being handed a payload here.</summary>
public sealed record WidgetTypeCatalogChangedNotification : INotification;
