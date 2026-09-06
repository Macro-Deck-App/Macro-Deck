using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconPackDeletedNotificationHandler : INotificationHandler<IconPackDeletedNotification>
{
	private readonly IUiTransport _uiTransport;

	public IconPackDeletedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(IconPackDeletedNotification notification, CancellationToken cancellationToken)
	{
		await _uiTransport.Send(new IconPackDeletedEvent
			{
				PackId = notification.PackId.ToString()
			},
			cancellationToken);
	}
}
