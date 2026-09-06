using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record ProfileDeletedNotification(Guid ProfileId) : INotification;
