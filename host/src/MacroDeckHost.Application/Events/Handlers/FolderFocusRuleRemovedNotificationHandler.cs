using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderFocusRuleRemovedNotificationHandler : INotificationHandler<FolderFocusRuleRemovedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IApplicationFocusCoordinator _coordinator;

	public FolderFocusRuleRemovedNotificationHandler(IUiTransport uiTransport, IApplicationFocusCoordinator coordinator)
	{
		_uiTransport = uiTransport;
		_coordinator = coordinator;
	}

	public async ValueTask Handle(FolderFocusRuleRemovedNotification notification, CancellationToken cancellationToken)
	{
		await _coordinator.OnRulesChanged(cancellationToken);

		var evt = new FolderFocusRuleRemovedEvent
		{
			FolderId = notification.FolderId.ToString(),
			RuleId = notification.RuleId.ToString()
		};

		await _uiTransport.SendToGroup(UiAdminGroups.Admin, evt, cancellationToken);
	}
}
