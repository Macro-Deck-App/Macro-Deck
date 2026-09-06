using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

/// <param name="DataChanged">Whether the save changed the widget's stored configuration, as opposed to
/// only where it sits or whether it is pinned. A move must not cost an open session its tree, and
/// dragging a widget across the grid publishes this on every step.</param>
public sealed record WidgetUpdatedNotification(WidgetEntity Widget, bool DataChanged = true) : INotification;
