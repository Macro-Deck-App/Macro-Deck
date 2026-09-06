using Mediator;

namespace MacroDeckHost.Application.Events;

/// <summary>A folder view was registered, unregistered, or an owner's views were all unregistered from
/// <c>IFolderViewRegistry</c> (issue #785). Marker only: subscribers re-derive whatever they need from the
/// registry themselves rather than being handed a payload here.</summary>
public sealed record FolderViewCatalogChangedNotification : INotification;
