namespace MacroDeck.Sdk.Companion;

public enum CompanionScreenshotMode
{
	/// <summary>Only the deck, without asking the device's user. Needs <see cref="CompanionDeviceCapabilities.ScreenshotDeck" />.</summary>
	Deck,

	/// <summary>
	/// The whole screen. Needs <see cref="CompanionDeviceCapabilities.ScreenshotFull" />, which the device grants
	/// only after its user agreed on the device.
	/// </summary>
	FullScreen
}
