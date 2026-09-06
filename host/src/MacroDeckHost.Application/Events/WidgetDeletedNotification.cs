using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetDeletedNotification(Guid WidgetId, Guid FolderId) : INotification;
