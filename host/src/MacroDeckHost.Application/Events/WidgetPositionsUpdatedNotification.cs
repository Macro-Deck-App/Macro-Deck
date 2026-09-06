using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetPositionsUpdatedNotification(
	Guid FolderId,
	IReadOnlyList<WidgetEntity> Widgets,
	IReadOnlyCollection<Guid> SizeChangedWidgetIds) : INotification;
