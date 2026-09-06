using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Plugins;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class PluginPairingRequestsChangedNotificationHandler
	: INotificationHandler<PluginPairingRequestsChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public PluginPairingRequestsChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(PluginPairingRequestsChangedNotification notification, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin,
			new PluginPairingRequestsChangedEvent(),
			cancellationToken));
}
