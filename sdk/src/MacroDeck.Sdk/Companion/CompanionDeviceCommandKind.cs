namespace MacroDeck.Sdk.Companion;

public enum CompanionDeviceCommandKind
{
	SetBrightness,
	SetOrientation,
	Vibrate,

	/// <summary>Needs <see cref="CompanionDeviceCapabilities.ScreenOn" />.</summary>
	ScreenOn,

	/// <summary>Needs <see cref="CompanionDeviceCapabilities.ScreenOff" />.</summary>
	ScreenOff,

	/// <summary>Brings Macro Deck to the front on the device. Needs <see cref="CompanionDeviceCapabilities.Focus" />.</summary>
	Focus
}
