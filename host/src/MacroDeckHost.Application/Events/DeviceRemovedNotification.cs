using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record DeviceRemovedNotification(Guid DeviceId) : INotification;
