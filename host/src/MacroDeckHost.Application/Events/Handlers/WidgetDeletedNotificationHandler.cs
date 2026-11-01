using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetDeletedNotificationHandler : INotificationHandler<WidgetDeletedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IUiSessionBroker _sessions;
	private readonly WidgetUiProviderRegistry _widgetUiProviders;

	public WidgetDeletedNotificationHandler(
		IUiTransport uiTransport,
		IUiSessionBroker sessions,
		WidgetUiProviderRegistry widgetUiProviders)
	{
		_uiTransport = uiTransport;
		_sessions = sessions;
		_widgetUiProviders = widgetUiProviders;
	}

	public async ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		DeletedWidgetCleanup.Run(_sessions, _widgetUiProviders, notification.WidgetId);

		var evt = new WidgetDeletedEvent
		{
			WidgetId = notification.WidgetId.ToString(),
			FolderId = notification.FolderId.ToString()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
