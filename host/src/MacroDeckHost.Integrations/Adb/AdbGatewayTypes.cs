namespace MacroDeckHost.Integrations.Adb;

public enum AdbGatewayDeviceState
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

public sealed record AdbGatewayDevice(
	string Serial,
	AdbGatewayDeviceState State,
	string? Model,
	string? Manufacturer,
	bool TunnelEstablished)
{
	public bool IsAuthorized => State is AdbGatewayDeviceState.Device;
}

public enum AdbGatewayDeviceChangeKind
{
	Connected,
	Disconnected,
	Authorized,
	Unauthorized,
	Online,
	Offline
}

public sealed record AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind Kind, AdbGatewayDevice Device);

public enum AdbGatewayKey
{
	Home,
	Back,
	Enter,
	Menu,
	Search,
	VolumeUp,
	VolumeDown,
	VolumeMute,
	MediaPlayPause,
	MediaNext,
	MediaPrevious,
	Power,
	Sleep,
	Wakeup,
	AppSwitch,
	DpadUp,
	DpadDown,
	DpadLeft,
	DpadRight,
	DpadCenter
}

public enum AdbGatewayRebootMode
{
	Normal,
	Recovery,
	Bootloader
}

public enum AdbGatewayFailureCode
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

public sealed record AdbGatewayResult(bool Success, AdbGatewayFailureCode? Failure, string? Message)
{
	public static AdbGatewayResult Ok() => new(true, null, null);

	public static AdbGatewayResult Fail(AdbGatewayFailureCode code, string? message = null)
		=> new(false, code, message);
}

public sealed record AdbGatewayProperties(
	int? BatteryLevel,
	bool? ScreenOn,
	bool? Locked,
	string? ForegroundPackage);
