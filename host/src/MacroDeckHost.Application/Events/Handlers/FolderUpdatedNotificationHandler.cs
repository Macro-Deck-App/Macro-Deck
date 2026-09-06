using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderUpdatedNotificationHandler : INotificationHandler<FolderUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IUiSessionBroker _sessions;

	public FolderUpdatedNotificationHandler(IUiTransport uiTransport, IUiSessionBroker sessions)
	{
		_uiTransport = uiTransport;
		_sessions = sessions;
	}

	public async ValueTask Handle(FolderUpdatedNotification notification, CancellationToken cancellationToken)
	{
		// Every widget in the folder builds its own edge clearance from the folder's corner radius, and
		// builds it into its tree (ADR 0064). A radius that changed therefore has to reach the widgets
		// themselves, and the only way a tree comes up to date is to be built again. Gated, because this
		// notification also carries a rename and a background change, and rebuilding a deck for one of
		// those would cost it its artwork and its animations for nothing.
		if (notification.CornerRadiusChanged)
		{
			foreach (var widget in notification.Folder.Widgets)
			{
				_sessions.InvalidateWidgetSessions(widget.Id);
			}
		}

		var evt = new FolderUpdatedEvent { Folder = FolderDtoMapper.MapToDto(notification.Folder) };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
