namespace MacroDeckHost.Application.Adb;

public sealed record AdbDeviceProperties(
	int? BatteryLevel,
	bool? ScreenOn,
	bool? Locked,
	string? ForegroundPackage,
	DateTimeOffset SampledAt);
