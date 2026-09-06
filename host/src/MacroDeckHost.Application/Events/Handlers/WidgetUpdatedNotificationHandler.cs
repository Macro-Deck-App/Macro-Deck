using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetUpdatedNotificationHandler : INotificationHandler<WidgetUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly LabelRenderChannel _renderQueue;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IUiSessionBroker _sessions;

	public WidgetUpdatedNotificationHandler(
		IUiTransport uiTransport,
		LabelRenderChannel renderQueue,
		IWidgetRenderSignals renderSignals,
		IUiSessionBroker sessions)
	{
		_uiTransport = uiTransport;
		_renderQueue = renderQueue;
		_renderSignals = renderSignals;
		_sessions = sessions;
	}

	public async ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		var widget = notification.Widget;

		_renderQueue.Enqueue(widget.Id);

		// Also how a plugin's WidgetAppearanceService recolouring reaches an open session's tile: the
		// stored data changed, so a session re-reads and re-parses it itself rather than this handler
		// trying to compute what changed.
		//
		// Not every session can. A session that built the configuration into its own tree - which is
		// every widget type that does not subscribe here, plugin widgets among them, since a plugin has
		// no way to observe stored data at all - would go on drawing the configuration it opened with,
		// leaving a saved edit sitting unapplied on every deck until something happened to reopen it.
		// Rebuilding is the only way that tree comes up to date, and reopening is how a client rebuilds.
		if (notification.DataChanged && !_renderSignals.RaiseDataChanged(widget))
		{
			_sessions.InvalidateWidgetSessions(widget.Id);
		}

		var evt = new WidgetUpdatedEvent
		{
			FolderId = widget.FolderId.ToString(),
			Widget = FolderDtoMapper.MapWidgetToDto(widget)
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
