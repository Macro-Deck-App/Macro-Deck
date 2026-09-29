using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetTypeFavoritesChangedNotificationHandler
	: INotificationHandler<WidgetTypeFavoritesChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public WidgetTypeFavoritesChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(
		WidgetTypeFavoritesChangedNotification notification,
		CancellationToken cancellationToken)
		=> await _uiTransport.SendToGroup(UiAdminGroups.Admin,
			new WidgetTypeFavoritesChangedEvent { TypeIds = notification.TypeIds },
			cancellationToken);
}
