using MacroDeckHost.Application.Store.Operations;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record StoreOperationProgressNotification(StoreOperation Operation) : INotification;
