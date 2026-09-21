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

	// adb names a device reached over the network by host:port, or by its mDNS service after wireless pairing.
	// A loopback host is an emulator on this machine (BlueStacks, WSA and similar), which still needs its tunnel.
	public bool IsNetworkConnection
		=> Serial.Contains("._adb-tls-connect._tcp", StringComparison.Ordinal) ||
			(Serial.LastIndexOf(':') is var colon and > 0 &&
				colon < Serial.Length - 1 &&
				Serial[(colon + 1)..].All(char.IsAsciiDigit) &&
				!IsLoopback(Serial[..colon]));

	private static bool IsLoopback(string host)
		=> host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
			(System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var address) &&
				System.Net.IPAddress.IsLoopback(address));
}
