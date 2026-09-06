using MacroDeckHost.Application.Deck;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class DevicePresenceChangedNotificationHandler : INotificationHandler<DevicePresenceChangedNotification>
{
	private readonly IApplicationFocusCoordinator _coordinator;

	public DevicePresenceChangedNotificationHandler(IApplicationFocusCoordinator coordinator)
	{
		_coordinator = coordinator;
	}

	public ValueTask Handle(DevicePresenceChangedNotification notification, CancellationToken cancellationToken)
		=> new(_coordinator.OnDevicePresenceChanged(notification.DeviceId, notification.Online, cancellationToken));
}
