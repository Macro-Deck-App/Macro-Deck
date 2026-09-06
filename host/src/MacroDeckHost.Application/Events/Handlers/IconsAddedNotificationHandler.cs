using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconsAddedNotificationHandler : INotificationHandler<IconsAddedNotification>
{
	private readonly IUiTransport _uiTransport;

	public IconsAddedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(IconsAddedNotification notification, CancellationToken cancellationToken)
	{
		await _uiTransport.Send(new IconsAddedEvent
			{
				BatchId = notification.BatchId?.ToString(),
				PackId = notification.PackId.ToString(),
				Icons = notification.Icons.Select(IconMapper.ToDto).ToList()
			},
			cancellationToken);
	}
}
