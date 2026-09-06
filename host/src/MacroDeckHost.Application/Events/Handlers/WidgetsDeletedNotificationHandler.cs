using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetsDeletedNotificationHandler : INotificationHandler<WidgetsDeletedNotification>
{
	private readonly IUiTransport _uiTransport;

	public WidgetsDeletedNotificationHandler(IUiTransport uiTransport) => _uiTransport = uiTransport;

	public async ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new WidgetsDeletedEvent
		{
			FolderId = notification.FolderId.ToString(),
			WidgetIds = notification.WidgetIds.Select(id => id.ToString()).ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
