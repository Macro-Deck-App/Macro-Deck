using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class AdbSettingsChangedUiRefreshHandler(IUiTransport transport, TimeProvider timeProvider)
	: INotificationHandler<AdbSettingsChangedNotification>
{
	public async ValueTask Handle(AdbSettingsChangedNotification notification, CancellationToken cancellationToken)
	{
		await transport.SendToGroup(UiAdminGroups.Admin,
			new AdbStateChangedEvent { ChangedAt = timeProvider.GetUtcNow() },
			cancellationToken);
	}
}
