using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IntegrationStateChangedNotification(string IntegrationId) : INotification;
