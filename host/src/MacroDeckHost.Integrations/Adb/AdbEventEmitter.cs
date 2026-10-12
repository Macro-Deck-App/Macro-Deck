using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Adb;

internal sealed class AdbEventEmitter
{
	private readonly IEventPublisher _publisher;

	public AdbEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void Publish(AdbDeviceChange change)
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
			payload["state"] = change.Device.Status.ToString();
		}

		_publisher.Publish(eventId, payload);
	}

	private static string? EventIdFor(AdbDeviceChangeKind kind) => kind switch
	{
		AdbDeviceChangeKind.Connected => AdbEventIds.DeviceConnected,
		AdbDeviceChangeKind.Disconnected => AdbEventIds.DeviceDisconnected,
		AdbDeviceChangeKind.Authorized => AdbEventIds.DeviceAuthorized,
		AdbDeviceChangeKind.Unauthorized => AdbEventIds.DeviceUnauthorized,
		AdbDeviceChangeKind.Online => AdbEventIds.DeviceOnline,
		AdbDeviceChangeKind.Offline => AdbEventIds.DeviceOffline,
		_ => null
	};
}
