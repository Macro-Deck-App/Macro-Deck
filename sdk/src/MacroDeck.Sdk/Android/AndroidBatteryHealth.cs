namespace MacroDeck.Sdk.Android;

public enum AndroidBatteryHealth
{
	Unknown = 0,
	Good = 1,
	Overheat = 2,
	Dead = 3,
	OverVoltage = 4,

	/// <summary>An unspecified failure reported by the device.</summary>
	Failure = 5,
	Cold = 6
}
