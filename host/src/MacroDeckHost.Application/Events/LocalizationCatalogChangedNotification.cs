using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record LocalizationCatalogChangedNotification(string Scope) : INotification;
