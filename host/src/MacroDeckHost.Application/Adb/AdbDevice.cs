namespace MacroDeckHost.Application.Adb;

public sealed record AdbDevice(
	string Serial,
	AdbDeviceState State,
	string? Model,
	string? Manufacturer,
	string? Product,
	string? TransportId,
	AdbTunnel? Tunnel,
	DateTimeOffset LastSeenAt)
{
	public bool IsAuthorized => State is AdbDeviceState.Device;
}
