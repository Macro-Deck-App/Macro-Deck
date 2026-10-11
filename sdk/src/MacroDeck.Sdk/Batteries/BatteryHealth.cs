namespace MacroDeck.Sdk.Batteries;

/// <summary>The battery's own health report. A value a reader does not know reads as <see cref="Unknown" />.</summary>
public enum BatteryHealth
{
	Unknown = 0,
	Good = 1,
	Overheat = 2,
	Dead = 3,
	OverVoltage = 4,
	Cold = 5,

	/// <summary>An unspecified failure reported by the device.</summary>
	Failure = 6
}
