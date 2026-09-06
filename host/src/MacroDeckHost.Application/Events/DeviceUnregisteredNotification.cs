using Mediator;

namespace MacroDeckHost.Application.Events;

/// <summary>
/// A provider withdrew a device, or stopped and withdrew all of them. Distinct from
/// <see cref="DevicePresenceChangedNotification" /> on purpose: withdrawing and going offline look
/// identical in the device's stored state, and only the former ends whatever the host was running for
/// it. The device itself is retained - see <see cref="DeviceRemovedNotification" /> for deletion.
/// </summary>
public sealed record DeviceUnregisteredNotification(Guid DeviceId) : INotification;
