using MacroDeckHost.Application.Store;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record StoreRegistryRefreshedNotification(StoreRegistryStatus Status) : INotification;
