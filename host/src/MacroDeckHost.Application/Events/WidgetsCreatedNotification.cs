using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetsCreatedNotification(Guid FolderId, IReadOnlyList<WidgetEntity> Widgets) : INotification;
