using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class StoreUpdatesUiNotificationHandler : INotificationHandler<StoreUpdatesChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public StoreUpdatesUiNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(StoreUpdatesChangedNotification notification, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin,
			new StoreUpdatesChangedEvent
			{
				Updates = notification.Updates.Select(StoreAvailableUpdateBodyFactory.Create).ToList()
			},
			cancellationToken));
}
