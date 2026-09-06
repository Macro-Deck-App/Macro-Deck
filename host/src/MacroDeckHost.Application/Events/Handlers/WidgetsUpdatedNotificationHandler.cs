using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetsUpdatedNotificationHandler : INotificationHandler<WidgetsUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly LabelRenderChannel _renderQueue;

	public WidgetsUpdatedNotificationHandler(IUiTransport uiTransport, LabelRenderChannel renderQueue)
	{
		_uiTransport = uiTransport;
		_renderQueue = renderQueue;
	}

	public async ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			_renderQueue.Enqueue(widget.Id);
		}

		var evt = new WidgetsUpdatedEvent
		{
			FolderId = notification.FolderId.ToString(),
			Widgets = notification.Widgets
				.Select(widget => FolderDtoMapper.MapWidgetToDto(widget))
				.ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
