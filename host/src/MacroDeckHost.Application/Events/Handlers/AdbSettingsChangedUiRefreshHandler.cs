using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class AdbSettingsChangedUiRefreshHandler(IUiTransport transport, TimeProvider timeProvider)
	: INotificationHandler<AdbSettingsChangedNotification>
{
	// No payload, like AdbBackgroundService: the event reaches every UI client, the settings are admin-only.
	public async ValueTask Handle(AdbSettingsChangedNotification notification, CancellationToken cancellationToken)
		=> await transport.Send(new AdbStateChangedEvent { ChangedAt = timeProvider.GetUtcNow() }, cancellationToken);
}
