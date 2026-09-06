using Mediator;

namespace MacroDeckHost.Application.Events;

/// <summary>A layout was registered, unregistered, or an owner's layouts were all unregistered from
/// <c>ILayoutRegistry</c> (issue #384). Marker only: subscribers re-derive whatever they need from the
/// registry or repositories themselves rather than being handed a payload here.</summary>
public sealed record LayoutCatalogChangedNotification : INotification;
