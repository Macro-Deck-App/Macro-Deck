namespace MacroDeckHost.Application.Adb;

public sealed record AdbBatteryReading(int Level, bool IsPluggedIn, AdbBatteryStatus Status, AdbBatteryHealth Health);

public enum AdbBatteryStatus
{
	Unknown,
	Charging,
	Discharging,
	NotCharging,
	Full
}

public enum AdbBatteryHealth
{
	Unknown,
	Good,
	Overheat,
	Dead,
	OverVoltage,
	Failure,
	Cold
}
