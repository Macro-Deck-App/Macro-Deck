namespace MacroDeck.Sdk.Android;

/// <summary>The battery as Android reports it, normalized so a plugin does not parse <c>dumpsys battery</c>.</summary>
public sealed record AndroidBatteryState
{
	/// <summary>0 to 100.</summary>
	public int Level { get; init; }

	/// <summary>True while a power source is connected, including when the battery is already full.</summary>
	public bool IsCharging { get; init; }

	public AndroidBatteryStatus Status { get; init; }

	public AndroidBatteryHealth Health { get; init; }
}
