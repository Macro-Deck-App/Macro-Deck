using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Plugins;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class PluginRuntimeChangedNotificationHandler : INotificationHandler<PluginRuntimeChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public PluginRuntimeChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(PluginRuntimeChangedNotification notification, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin, new PluginRuntimeChangedEvent(), cancellationToken));
}
