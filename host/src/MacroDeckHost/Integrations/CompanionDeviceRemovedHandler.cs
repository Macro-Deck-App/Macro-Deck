using MacroDeckHost.Application.Events;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations;

internal sealed class CompanionDeviceRemovedHandler : INotificationHandler<DeviceRemovedNotification>
{
	private readonly CompanionDeviceRegistry _devices;
	private readonly ILogger _logger;

	public CompanionDeviceRemovedHandler(CompanionDeviceRegistry devices, ILogger logger)
	{
		_devices = devices;
		_logger = logger;
	}

	// A device removal, a stale purge and the login that runs the purge must never fail because of this.
	public async ValueTask Handle(DeviceRemovedNotification notification, CancellationToken cancellationToken)
	{
		try
		{
			await _devices.RemoveDeviceAsync(notification.DeviceId, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Could not remove the Companion configuration of device {DeviceId}",
				notification.DeviceId);
		}
	}
}
