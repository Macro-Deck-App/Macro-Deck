using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IntegrationCatalogChangedNotification(string IntegrationId) : INotification;
