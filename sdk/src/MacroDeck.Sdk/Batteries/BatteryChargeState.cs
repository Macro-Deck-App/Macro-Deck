namespace MacroDeck.Sdk.Batteries;

/// <summary>Whether a battery device is drawing power. A value a reader does not know reads as <see cref="Unknown" />.</summary>
public enum BatteryChargeState
{
	Unknown = 0,
	Discharging = 1,
	Charging = 2,
	Full = 3,

	/// <summary>Connected to power but not charging.</summary>
	NotCharging = 4
}
