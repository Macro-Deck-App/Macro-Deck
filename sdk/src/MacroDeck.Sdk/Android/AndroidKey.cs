namespace MacroDeck.Sdk.Android;

/// <summary>A key <see cref="IAndroidDevice.SendKeyAsync" /> presses, named after its Android key event.</summary>
public enum AndroidKey
{
	Home = 0,
	Back = 1,
	Enter = 2,
	Menu = 3,
	Search = 4,
	VolumeUp = 5,
	VolumeDown = 6,
	VolumeMute = 7,
	MediaPlayPause = 8,
	MediaNext = 9,
	MediaPrevious = 10,

	/// <summary>Toggles the screen like the power button.</summary>
	Power = 11,

	/// <summary>Turns the screen off; does nothing when it is already off.</summary>
	Sleep = 12,

	/// <summary>Turns the screen on; does nothing when it is already on.</summary>
	Wakeup = 13,

	/// <summary>Opens the recent apps.</summary>
	AppSwitch = 14,
	DpadUp = 15,
	DpadDown = 16,
	DpadLeft = 17,
	DpadRight = 18,
	DpadCenter = 19
}
