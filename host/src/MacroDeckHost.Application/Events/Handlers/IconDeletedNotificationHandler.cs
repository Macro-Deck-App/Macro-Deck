using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconDeletedNotificationHandler : INotificationHandler<IconDeletedNotification>
{
	private readonly IUiTransport _uiTransport;

	public IconDeletedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(IconDeletedNotification notification, CancellationToken cancellationToken)
	{
		await _uiTransport.Send(new IconDeletedEvent
			{
				IconId = notification.IconId.ToString(),
				PackId = notification.PackId.ToString()
			},
			cancellationToken);
	}
}
