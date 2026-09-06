using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetsUpdatedNotification(Guid FolderId, IReadOnlyList<WidgetEntity> Widgets) : INotification;
