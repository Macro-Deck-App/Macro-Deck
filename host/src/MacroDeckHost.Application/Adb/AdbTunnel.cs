namespace MacroDeckHost.Application.Adb;

public sealed record AdbTunnel(
	bool Established,
	int DevicePort,
	int HostPort,
	AdbFailureCode? Failure,
	string? FailureMessage);
