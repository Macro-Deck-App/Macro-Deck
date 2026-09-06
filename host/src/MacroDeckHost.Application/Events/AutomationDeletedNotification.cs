using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record AutomationDeletedNotification(Guid AutomationId) : INotification;
