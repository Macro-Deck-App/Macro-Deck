namespace MacroDeck.Sdk.Android;

/// <summary>
/// What an <see cref="AndroidDeviceState.Offline" /> device is doing instead of running Android normally, as adb
/// reports it. Every other state is <see cref="Normal" />.
/// </summary>
public enum AndroidDeviceMode
{
	/// <summary>Android runs normally, or the device is simply not answering.</summary>
	Normal = 0,

	Bootloader = 1,

	Recovery = 2,

	/// <summary>Waiting for an update to be sideloaded from recovery.</summary>
	Sideload = 3,

	/// <summary>adb reported a state this Macro Deck does not recognise.</summary>
	Unknown = 4
}
