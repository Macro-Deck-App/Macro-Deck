using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record DeviceChangedNotification(Guid DeviceId) : INotification;
