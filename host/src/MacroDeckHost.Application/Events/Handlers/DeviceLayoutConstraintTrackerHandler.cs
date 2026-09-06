using MacroDeckHost.Application.Layouts;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>Keeps <see cref="DeviceLayoutConstraintTracker" /> in sync (issue #384): a device claiming
/// or dropping a profile, or the layout catalogue changing, can all change what a profile's grid is
/// constrained to, so all of them trigger the same full refresh.</summary>
public sealed class DeviceLayoutConstraintTrackerHandler :
	INotificationHandler<DeviceChangedNotification>,
	INotificationHandler<DeviceRemovedNotification>,
	INotificationHandler<DeviceUnregisteredNotification>,
	INotificationHandler<DevicePresenceChangedNotification>,
	INotificationHandler<LayoutCatalogChangedNotification>
{
	private readonly DeviceLayoutConstraintTracker _tracker;

	public DeviceLayoutConstraintTrackerHandler(DeviceLayoutConstraintTracker tracker)
	{
		_tracker = tracker;
	}

	public ValueTask Handle(DeviceChangedNotification notification, CancellationToken cancellationToken)
		=> new(_tracker.RefreshAsync(cancellationToken));

	public ValueTask Handle(DeviceRemovedNotification notification, CancellationToken cancellationToken)
		=> new(_tracker.RefreshAsync(cancellationToken));

	public ValueTask Handle(DeviceUnregisteredNotification notification, CancellationToken cancellationToken)
		=> new(_tracker.RefreshAsync(cancellationToken));

	public ValueTask Handle(DevicePresenceChangedNotification notification, CancellationToken cancellationToken)
		=> new(_tracker.RefreshAsync(cancellationToken));

	public ValueTask Handle(LayoutCatalogChangedNotification notification, CancellationToken cancellationToken)
		=> new(_tracker.RefreshAsync(cancellationToken));
}
