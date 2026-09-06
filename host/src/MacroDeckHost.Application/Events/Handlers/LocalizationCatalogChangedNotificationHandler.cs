using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Localization;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class LocalizationCatalogChangedNotificationHandler
	: INotificationHandler<LocalizationCatalogChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public LocalizationCatalogChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(LocalizationCatalogChangedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new LocalizationCatalogChangedEvent { Scope = notification.Scope };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
