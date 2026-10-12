namespace MacroDeck.Sdk.Batteries;

/// <summary>The bounds Macro Deck enforces on published battery devices.</summary>
public static class BatteryLimits
{
	/// <summary>The most devices one provider may have published at a time. A publish that would exceed it is refused as a whole.</summary>
	public const int MaxDevicesPerProvider = 64;

	public const int MaxIdLength = 128;

	public const int MaxNameLength = 128;

	public const int MaxKindLength = 32;
}
