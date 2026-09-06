using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class DeveloperModeChangedNotificationHandler : INotificationHandler<DeveloperModeChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public DeveloperModeChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(DeveloperModeChangedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new DeveloperSettingsChangedEvent { Enabled = notification.Enabled };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
