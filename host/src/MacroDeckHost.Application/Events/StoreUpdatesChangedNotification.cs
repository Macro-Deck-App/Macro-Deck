using MacroDeckHost.Application.Store.Updates;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record StoreUpdatesChangedNotification(IReadOnlyList<StoreAvailableUpdate> Updates) : INotification;
