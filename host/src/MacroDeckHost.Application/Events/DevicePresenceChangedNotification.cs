using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record DevicePresenceChangedNotification(Guid DeviceId, bool Online) : INotification;
