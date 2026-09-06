using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Adb;

internal sealed class AdbEventEmitter
{
	private readonly IEventPublisher _publisher;

	public AdbEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void Publish(AdbGatewayDeviceChange change)
	{
		var eventId = EventIdFor(change.Kind);
		if (eventId is null)
		{
			return;
		}

		var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["serial"] = change.Device.Serial,
			["model"] = change.Device.Model
		};

		if (string.Equals(eventId, AdbEventIds.DeviceConnected, StringComparison.Ordinal))
		{
			payload["manufacturer"] = change.Device.Manufacturer;
			payload["state"] = change.Device.State.ToString();
		}

		_publisher.Publish(eventId, payload);
	}

	private static string? EventIdFor(AdbGatewayDeviceChangeKind kind) => kind switch
	{
		AdbGatewayDeviceChangeKind.Connected => AdbEventIds.DeviceConnected,
		AdbGatewayDeviceChangeKind.Disconnected => AdbEventIds.DeviceDisconnected,
		AdbGatewayDeviceChangeKind.Authorized => AdbEventIds.DeviceAuthorized,
		AdbGatewayDeviceChangeKind.Unauthorized => AdbEventIds.DeviceUnauthorized,
		AdbGatewayDeviceChangeKind.Online => AdbEventIds.DeviceOnline,
		AdbGatewayDeviceChangeKind.Offline => AdbEventIds.DeviceOffline,
		_ => null
	};
}
