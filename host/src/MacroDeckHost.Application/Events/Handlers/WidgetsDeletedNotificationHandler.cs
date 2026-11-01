using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetsDeletedNotificationHandler : INotificationHandler<WidgetsDeletedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IUiSessionBroker _sessions;
	private readonly WidgetUiProviderRegistry _widgetUiProviders;

	public WidgetsDeletedNotificationHandler(
		IUiTransport uiTransport,
		IUiSessionBroker sessions,
		WidgetUiProviderRegistry widgetUiProviders)
	{
		_uiTransport = uiTransport;
		_sessions = sessions;
		_widgetUiProviders = widgetUiProviders;
	}

	public async ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widgetId in notification.WidgetIds)
		{
			DeletedWidgetCleanup.Run(_sessions, _widgetUiProviders, widgetId);
		}

		var evt = new WidgetsDeletedEvent
		{
			FolderId = notification.FolderId.ToString(),
			WidgetIds = notification.WidgetIds.Select(id => id.ToString()).ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
