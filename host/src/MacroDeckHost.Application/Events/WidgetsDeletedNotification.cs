using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetsDeletedNotification(Guid FolderId, IReadOnlyList<Guid> WidgetIds) : INotification;
