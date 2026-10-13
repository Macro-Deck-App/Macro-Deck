using MacroDeckHost.Application.Events;
using Mediator;

namespace MacroDeckHost.Integrations;

internal sealed class CompanionDeviceEntriesOwnerHandler : INotificationHandler<IntegrationStateChangedNotification>
{
	private readonly CompanionDeviceRegistry _devices;

	public CompanionDeviceEntriesOwnerHandler(CompanionDeviceRegistry devices)
	{
		_devices = devices;
	}

	public ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
	{
		_devices.IntegrationStateChanged(notification.IntegrationId);
		return ValueTask.CompletedTask;
	}
}
