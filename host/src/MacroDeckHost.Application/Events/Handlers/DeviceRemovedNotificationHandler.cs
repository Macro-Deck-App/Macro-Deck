using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class DeviceRemovedNotificationHandler : INotificationHandler<DeviceRemovedNotification>
{
	private readonly IUiTransport _uiTransport;

	public DeviceRemovedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(DeviceRemovedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new DeviceRemovedEvent { DeviceId = notification.DeviceId.ToString() };
		return new ValueTask(_uiTransport.SendToGroup(UiAdminGroups.Admin, evt, cancellationToken));
	}
}
