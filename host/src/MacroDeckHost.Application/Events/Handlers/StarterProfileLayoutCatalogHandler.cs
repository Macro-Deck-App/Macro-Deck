using MacroDeckHost.Application.Devices;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class StarterProfileLayoutCatalogHandler : INotificationHandler<LayoutCatalogChangedNotification>
{
	private readonly ProviderDeviceStarterProfiles _starterProfiles;

	public StarterProfileLayoutCatalogHandler(ProviderDeviceStarterProfiles starterProfiles)
	{
		_starterProfiles = starterProfiles;
	}

	public ValueTask Handle(LayoutCatalogChangedNotification notification, CancellationToken cancellationToken)
	{
		return new ValueTask(_starterProfiles.ProvidePendingAsync());
	}
}
