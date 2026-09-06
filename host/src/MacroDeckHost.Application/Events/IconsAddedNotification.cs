using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IconsAddedNotification(Guid? BatchId, Guid PackId, IReadOnlyList<IconEntity> Icons)
	: INotification;
