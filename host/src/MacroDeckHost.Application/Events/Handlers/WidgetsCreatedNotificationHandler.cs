using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetsCreatedNotificationHandler : INotificationHandler<WidgetsCreatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public WidgetsCreatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new WidgetsCreatedEvent
		{
			FolderId = notification.FolderId.ToString(),
			Widgets = notification.Widgets
				.Select(widget => FolderDtoMapper.MapWidgetToDto(widget))
				.ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
