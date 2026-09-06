using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>
/// Everything that can change what a device renders funnels into one rebuild. Which notification
/// arrived never decides whether a device is pushed to - the rebuilt surface being different from the
/// last one pushed does. That is what keeps a revision count stable across edits a device cannot see,
/// and what keeps this list from having to enumerate "render-relevant" fields.
/// </summary>
public abstract class DeviceSurfaceInvalidationHandler<TNotification>
	: INotificationHandler<TNotification>
	where TNotification : INotification
{
	private readonly IDeviceSurfaceService _surfaces;

	protected DeviceSurfaceInvalidationHandler(IDeviceSurfaceService surfaces)
	{
		_surfaces = surfaces;
	}

	public ValueTask Handle(TNotification notification, CancellationToken cancellationToken)
		=> new(_surfaces.InvalidateAsync(cancellationToken));
}

public sealed class WidgetCreatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetCreatedNotification>(surfaces);

public sealed class WidgetUpdatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetUpdatedNotification>(surfaces);

public sealed class WidgetDeletedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetDeletedNotification>(surfaces);

public sealed class WidgetsCreatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetsCreatedNotification>(surfaces);

public sealed class WidgetsUpdatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetsUpdatedNotification>(surfaces);

public sealed class WidgetsDeletedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetsDeletedNotification>(surfaces);

public sealed class WidgetPositionsUpdatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<WidgetPositionsUpdatedNotification>(surfaces);

public sealed class FolderCreatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<FolderCreatedNotification>(surfaces);

public sealed class FolderUpdatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<FolderUpdatedNotification>(surfaces);

public sealed class FolderDeletedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<FolderDeletedNotification>(surfaces);

public sealed class FoldersReorderedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<FoldersReorderedNotification>(surfaces);

public sealed class ProfileUpdatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<ProfileUpdatedNotification>(surfaces);

public sealed class ProfileDeletedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<ProfileDeletedNotification>(surfaces);

public sealed class IconUpdatedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<IconUpdatedNotification>(surfaces);

public sealed class IconDeletedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	: DeviceSurfaceInvalidationHandler<IconDeletedNotification>(surfaces);

/// <summary>
/// Opens the session once a provider device is registered, and rebuilds an open one - a startup
/// profile reassigned elsewhere reaches a device only through this.
/// </summary>
public sealed class DeviceChangedDeviceSurfaceHandler : INotificationHandler<DeviceChangedNotification>
{
	private readonly IDeviceSurfaceService _surfaces;
	private readonly IServiceScopeFactory _scopeFactory;

	public DeviceChangedDeviceSurfaceHandler(IDeviceSurfaceService surfaces, IServiceScopeFactory scopeFactory)
	{
		_surfaces = surfaces;
		_scopeFactory = scopeFactory;
	}

	public async ValueTask Handle(DeviceChangedNotification notification, CancellationToken cancellationToken)
	{
		if (_surfaces.IsOpen(notification.DeviceId))
		{
			await _surfaces.InvalidateAsync(notification.DeviceId, cancellationToken);
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

		if (await devices.GetById(notification.DeviceId) is not { } device || !IsProviderDevice(device))
		{
			return;
		}

		await _surfaces.OpenAsync(device.Id, device.ProviderId!, device.ProviderDeviceId!, cancellationToken);
	}

	private static bool IsProviderDevice(DeviceEntity device)
		=> device.IsProviderDevice && !string.IsNullOrEmpty(device.ProviderDeviceId);
}

public sealed class DeviceUnregisteredDeviceSurfaceHandler : INotificationHandler<DeviceUnregisteredNotification>
{
	private readonly IDeviceSurfaceService _surfaces;

	public DeviceUnregisteredDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	{
		_surfaces = surfaces;
	}

	public ValueTask Handle(DeviceUnregisteredNotification notification, CancellationToken cancellationToken)
		=> new(_surfaces.CloseAsync(notification.DeviceId, "unregistered", cancellationToken));
}

public sealed class DeviceRemovedDeviceSurfaceHandler : INotificationHandler<DeviceRemovedNotification>
{
	private readonly IDeviceSurfaceService _surfaces;

	public DeviceRemovedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	{
		_surfaces = surfaces;
	}

	public ValueTask Handle(DeviceRemovedNotification notification, CancellationToken cancellationToken)
		=> new(_surfaces.CloseAsync(notification.DeviceId, "removed", cancellationToken));
}

/// <summary>Going offline keeps the session and its place; it only stops the pushing.</summary>
public sealed class DevicePresenceChangedDeviceSurfaceHandler : INotificationHandler<DevicePresenceChangedNotification>
{
	private readonly IDeviceSurfaceService _surfaces;

	public DevicePresenceChangedDeviceSurfaceHandler(IDeviceSurfaceService surfaces)
	{
		_surfaces = surfaces;
	}

	public ValueTask Handle(DevicePresenceChangedNotification notification, CancellationToken cancellationToken)
		=> new(_surfaces.SetPresenceAsync(notification.DeviceId, notification.Online, cancellationToken));
}
