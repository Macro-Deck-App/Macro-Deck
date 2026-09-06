using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconUpdatedNotificationHandler : INotificationHandler<IconUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public IconUpdatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(IconUpdatedNotification notification, CancellationToken cancellationToken)
	{
		await _uiTransport.Send(new IconUpdatedEvent
			{
				Icon = IconMapper.ToDto(notification.Icon)
			},
			cancellationToken);
	}
}
