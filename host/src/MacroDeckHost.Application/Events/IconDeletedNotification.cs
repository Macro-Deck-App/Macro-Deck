using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IconDeletedNotification(Guid IconId, Guid PackId) : INotification;
