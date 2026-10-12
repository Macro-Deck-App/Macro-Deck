namespace MacroDeck.Sdk.Companion;

/// <summary>
/// The capability names a Companion device reports in <see cref="CompanionDeviceState.Capabilities" /> and
/// <see cref="CompanionDeviceState.RequestableCapabilities" />. Later Macro Deck versions may report names that are
/// not listed here; treat an unknown name as a capability this plugin does not use.
/// </summary>
public static class CompanionDeviceCapabilities
{
	/// <summary>The device can switch its screen on, see <see cref="CompanionDeviceCommandKind.ScreenOn" />.</summary>
	public const string ScreenOn = "screenOn";

	/// <summary>The device can switch its screen off, see <see cref="CompanionDeviceCommandKind.ScreenOff" />.</summary>
	public const string ScreenOff = "screenOff";

	/// <summary>The device can capture its deck, see <see cref="CompanionScreenshotMode.Deck" />.</summary>
	public const string ScreenshotDeck = "screenshotDeck";

	/// <summary>The device can capture its whole screen, see <see cref="CompanionScreenshotMode.FullScreen" />.</summary>
	public const string ScreenshotFull = "screenshotFull";

	/// <summary>The device can bring Macro Deck to the front, see <see cref="CompanionDeviceCommandKind.Focus" />.</summary>
	public const string Focus = "focus";

	/// <summary>The device reports <see cref="CompanionDeviceState.NetworkName" />.</summary>
	public const string NetworkName = "networkName";

	/// <summary>The device reports <see cref="CompanionDeviceState.CpuUsagePercent" />.</summary>
	public const string Cpu = "cpu";

	/// <summary>The device reports <see cref="CompanionDeviceState.MemoryUsedPercent" />.</summary>
	public const string Memory = "memory";
}
