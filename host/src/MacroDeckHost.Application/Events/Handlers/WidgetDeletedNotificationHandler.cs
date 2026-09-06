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
		// A deleted widget has no configuration left to rebuild a tree from, so its sessions are closed
		// outright rather than invalidated for a reopen - see IUiSessionBroker.CloseWidgetSessions.
		_sessions.CloseWidgetSessions(notification.WidgetId, "The widget was deleted.");

		// The synthetic per-widget provider ids (widget:<id>:*, widget-config:<id>:*) this widget ever
		// resolved to will never be asked for again once it is gone, so their cached adapters are forgotten
		// here rather than left in WidgetUiProviderRegistry for the life of the host.
		_widgetUiProviders.EvictWidget(notification.WidgetId);

		var evt = new WidgetDeletedEvent
		{
			WidgetId = notification.WidgetId.ToString(),
			FolderId = notification.FolderId.ToString()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
