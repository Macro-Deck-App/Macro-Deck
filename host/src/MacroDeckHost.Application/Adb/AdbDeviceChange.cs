namespace MacroDeckHost.Application.Adb;

public enum AdbDeviceChangeKind
{
	Connected,
	Disconnected,
	Authorized,
	Unauthorized,
	Online,
	Offline
}

public sealed record AdbDeviceChange(AdbDeviceChangeKind Kind, AdbDevice Device, AdbDeviceState? PreviousState);
