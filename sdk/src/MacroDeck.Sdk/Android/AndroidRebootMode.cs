namespace MacroDeck.Sdk.Android;

/// <summary>What <see cref="IAndroidDevice.RebootAsync" /> restarts the device into.</summary>
public enum AndroidRebootMode
{
	/// <summary>Android, as after switching the device off and on.</summary>
	Normal = 0,

	Recovery = 1,

	Bootloader = 2
}
