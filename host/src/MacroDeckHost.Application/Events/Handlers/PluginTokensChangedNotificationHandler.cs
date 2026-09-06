using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Plugins;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class PluginTokensChangedNotificationHandler : INotificationHandler<PluginTokensChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public PluginTokensChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(PluginTokensChangedNotification notification, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin, new PluginTokensChangedEvent(), cancellationToken));
}
