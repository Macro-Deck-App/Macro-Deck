namespace MacroDeckHost.Application.Adb;

public enum AdbDeviceState
{
	Unknown,

	Device,

	Unauthorized,
	Offline,
	Authorizing,
	NoPermissions,
	Bootloader,
	Recovery,
	Sideload,

	Disconnected
}
