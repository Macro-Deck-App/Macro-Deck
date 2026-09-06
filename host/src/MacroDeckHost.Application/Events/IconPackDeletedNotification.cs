using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IconPackDeletedNotification(Guid PackId) : INotification;
