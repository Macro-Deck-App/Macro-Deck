namespace MacroDeck.Sdk.Devices;

/// <summary>
/// What a registered device can do, in terms every device kind shares. Counts are what the device
/// physically has, not what a profile currently uses.
/// </summary>
/// <remarks>
/// Deliberately coarse. Anything specific to one device family belongs in <see cref="Extra" /> until the
/// layout abstraction describes it: a layout reference, not this record, is what will eventually carry
/// regions and their geometry.
/// </remarks>
public sealed record DeviceCapabilities
{
	/// <summary>A device that declares nothing. Registering with this is valid.</summary>
	public static readonly DeviceCapabilities None = new();

	/// <summary>Number of pressable keys or buttons.</summary>
	public int KeyCount { get; init; }

	/// <summary>Number of rotary encoders or dials.</summary>
	public int DialCount { get; init; }

	/// <summary>Number of addressable displays or display regions.</summary>
	public int DisplayCount { get; init; }

	/// <summary>Whether the device can show images.</summary>
	public bool SupportsImages { get; init; }

	/// <summary>Whether the device can show text.</summary>
	public bool SupportsText { get; init; }

	/// <summary>Provider-defined extras. Keys and values are opaque to the host.</summary>
	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
