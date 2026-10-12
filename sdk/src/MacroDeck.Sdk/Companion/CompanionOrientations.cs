namespace MacroDeck.Sdk.Companion;

/// <summary>Screen orientations of a Companion device.</summary>
public static class CompanionOrientations
{
	/// <summary>Follow the device's rotation. Only valid for <see cref="CompanionDeviceCommand.SetOrientation" />.</summary>
	public const string Automatic = "automatic";

	public const string Portrait = "portrait";

	public const string Landscape = "landscape";
}
