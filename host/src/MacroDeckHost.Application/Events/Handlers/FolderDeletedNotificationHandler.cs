using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderDeletedNotificationHandler : INotificationHandler<FolderDeletedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IApplicationFocusCoordinator _coordinator;

	public FolderDeletedNotificationHandler(IUiTransport uiTransport, IApplicationFocusCoordinator coordinator)
	{
		_uiTransport = uiTransport;
		_coordinator = coordinator;
	}

	public async ValueTask Handle(FolderDeletedNotification notification, CancellationToken cancellationToken)
	{
		await _coordinator.OnRulesChanged(cancellationToken);

		var evt = new FolderDeletedEvent { FolderId = notification.FolderId.ToString() };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
