using MacroDeckHost.Application.Devices.Surfaces;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class AppearanceChangedKeyImageHandler(IDeviceSurfaceService surfaces)
	: INotificationHandler<AppearanceChangedNotification>
{
	public ValueTask Handle(AppearanceChangedNotification notification, CancellationToken cancellationToken)
	{
		return new ValueTask(surfaces.RefreshKeyImagesAsync(cancellationToken));
	}
}

public sealed class LocalizationCultureChangedKeyImageHandler(IDeviceSurfaceService surfaces)
	: INotificationHandler<LocalizationCultureChangedNotification>
{
	public ValueTask Handle(LocalizationCultureChangedNotification notification, CancellationToken cancellationToken)
	{
		return new ValueTask(surfaces.RefreshKeyImagesAsync(cancellationToken));
	}
}
