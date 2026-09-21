namespace MacroDeck.Sdk.Android;

public enum AndroidBatteryStatus
{
	Unknown = 0,
	Charging = 1,
	Discharging = 2,

	/// <summary>Connected to power but not charging.</summary>
	NotCharging = 3,
	Full = 4
}
