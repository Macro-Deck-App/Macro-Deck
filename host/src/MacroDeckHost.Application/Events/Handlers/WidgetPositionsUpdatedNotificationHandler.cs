using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetPositionsUpdatedNotificationHandler : INotificationHandler<WidgetPositionsUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly LabelRenderChannel _renderQueue;

	public WidgetPositionsUpdatedNotificationHandler(IUiTransport uiTransport, LabelRenderChannel renderQueue)
	{
		_uiTransport = uiTransport;
		_renderQueue = renderQueue;
	}

	public async ValueTask Handle(WidgetPositionsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widgetId in notification.SizeChangedWidgetIds)
		{
			_renderQueue.Enqueue(widgetId);
		}

		var evt = new WidgetPositionsUpdatedEvent
		{
			FolderId = notification.FolderId.ToString(),
			Widgets = notification.Widgets
				.Select(widget => FolderDtoMapper.MapWidgetToDto(widget))
				.ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
