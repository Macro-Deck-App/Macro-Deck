namespace MacroDeckHost.Application.Adb;

public enum AdbFailureCode
{
	Disabled,
	ExecutableNotFound,
	ServerUnreachable,
	DeviceNotFound,
	DeviceUnauthorized,
	DeviceOffline,
	Timeout,
	InvalidParameter,
	CommandFailed,
	Unsupported
}
