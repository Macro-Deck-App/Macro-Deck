namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.ScreenSaver" /> surface carries
/// to say which device is showing the screensaver and which of the provider's screensavers it selected.
/// </summary>
/// <remarks>
/// <see cref="Configuration" /> travels on the surface for the same reason
/// <see cref="UiFolderSurfaceAttributes.Configuration" /> does: a provider outside the host cannot read the
/// host's stored devices, so what the user configured has to arrive with the request rather than be looked
/// up. Macro Deck never interprets it - it is written and read only by the provider that owns the screensaver.
/// </remarks>
public static class UiScreenSaverSurfaceAttributes
{
	/// <summary>The device showing the screensaver.</summary>
	public const string DeviceId = "deviceId";

	/// <summary>The qualified id of the screensaver the device selected, so a provider offering several
	/// declines one it does not serve rather than guessing from the configuration's shape.</summary>
	public const string ScreenSaverId = "screenSaverId";

	/// <summary>The screensaver's configuration, as the JSON object it is stored as. Absent reads as empty - a
	/// screensaver whose provider declares no configuration is opened without one.</summary>
	public const string Configuration = "configuration";
}
