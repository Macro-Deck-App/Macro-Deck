using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class StoreRegistryStatusUiNotificationHandler : INotificationHandler<StoreRegistryRefreshedNotification>
{
	private readonly IUiTransport _uiTransport;

	public StoreRegistryStatusUiNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(StoreRegistryRefreshedNotification notification, CancellationToken cancellationToken)
	{
		await _uiTransport.SendToGroup(UiAdminGroups.Admin,
			new StoreRegistryStatusChangedEvent
				{ Registry = StoreRegistryStatusBodyFactory.Create(notification.Status) },
			cancellationToken);
		await _uiTransport.SendToGroup(UiAdminGroups.Admin, new StoreCatalogChangedEvent(), cancellationToken);
	}
}
