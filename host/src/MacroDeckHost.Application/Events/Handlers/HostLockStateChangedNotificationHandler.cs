using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class HostLockStateChangedNotificationHandler : INotificationHandler<HostLockStateChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public HostLockStateChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(HostLockStateChangedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new HostLockStateChangedEvent
		{
			Locked = notification.Locked,
			LockScreenEnabled = notification.LockScreenEnabled,
			Supported = notification.Supported
		};
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
