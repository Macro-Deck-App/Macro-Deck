namespace MacroDeck.Sdk.Android;

/// <summary>
/// What the device reports about itself. Any value can be <c>null</c> until the device is online. Two
/// instances are equal when every value, <see cref="StableId" /> included, is equal.
/// </summary>
public sealed record AndroidDeviceInfo(string? Model, string? Manufacturer, string? Product)
{
	/// <summary>
	/// The same id for the same hardware over USB and Wi-Fi: a keyed hash of the device's hardware serial, with a
	/// key that is unique to this Macro Deck installation, so it cannot be matched against a serial or across
	/// installations. <c>null</c> until Macro Deck has read it, and always on a host that does not report it.
	/// </summary>
	public string? StableId { get; init; }
}
