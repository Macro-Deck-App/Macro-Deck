using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetCreatedNotificationHandler : INotificationHandler<WidgetCreatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public WidgetCreatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		var widget = notification.Widget;
		var evt = new WidgetCreatedEvent
		{
			FolderId = widget.FolderId.ToString(),
			Widget = FolderDtoMapper.MapWidgetToDto(widget)
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
