namespace MacroDeck.Sdk.Android;

public enum AndroidDeviceState
{
	/// <summary>Attached but not answering, or in a mode such as the bootloader.</summary>
	Offline = 0,

	/// <summary>Waiting for the user to confirm the authorization prompt on the device.</summary>
	Connecting = 1,

	/// <summary>Ready for operations.</summary>
	Online = 2,

	/// <summary>The device has not authorized this computer, or the computer lacks USB permissions.</summary>
	Unauthorized = 3
}
