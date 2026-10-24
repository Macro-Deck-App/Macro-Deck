namespace MacroDeck.Sdk.Companion;

/// <summary>
/// What a Companion device last reported about itself. Values Macro Deck could not read, or that the device does
/// not report, are <c>null</c>. Percentages are between 0 and 100 and texts are at most 64 characters long.
/// </summary>
public sealed record CompanionDeviceState
{
	public int? BatteryLevelPercent { get; init; }

	public bool IsCharging { get; init; }

	/// <summary><see cref="CompanionOrientations.Portrait" />, <see cref="CompanionOrientations.Landscape" /> or <c>null</c>.</summary>
	public string? Orientation { get; init; }

	public int? ScreenBrightnessPercent { get; init; }

	public string? Model { get; init; }

	/// <summary>The operating system and its version, for example <c>Android 14</c>.</summary>
	public string? Platform { get; init; }

	/// <summary>The version of the Companion app.</summary>
	public string? AppVersion { get; init; }

	/// <summary>
	/// One of <see cref="CompanionFormFactors" />, as the device declared it when it signed in, or <c>null</c> when it
	/// declared none or the host predates this property.
	/// </summary>
	public string? FormFactor { get; init; }

	/// <summary>The capabilities the device can use right now, from <see cref="CompanionDeviceCapabilities" />.</summary>
	public IReadOnlySet<string> Capabilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// Capabilities the device can use after it asks its user for permission. A request that needs one shows that
	/// prompt on the device and fails with <see cref="CompanionDeviceFailure.ConsentDenied" /> when the user declines.
	/// Never contains a capability of <see cref="Capabilities" />.
	/// </summary>
	public IReadOnlySet<string> RequestableCapabilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>
	/// Whether the device confirms commands. Only then can
	/// <see cref="ICompanionDeviceApi.RequestAsync" /> report a command as done or failed; a device that does not
	/// confirm leaves every request to time out.
	/// </summary>
	public bool ConfirmsCommands { get; init; }

	/// <summary>Whether Macro Deck is in front on the device.</summary>
	public bool? InFocus { get; init; }

	/// <summary>One of <see cref="CompanionNetworkTypes" />, or <c>null</c>.</summary>
	public string? NetworkType { get; init; }

	public bool? NetworkMetered { get; init; }

	/// <summary>Whether the device's network reaches the internet.</summary>
	public bool? NetworkValidated { get; init; }

	/// <summary>The Wi-Fi network's name, reported only with <see cref="CompanionDeviceCapabilities.NetworkName" />.</summary>
	public string? NetworkName { get; init; }

	public int? CpuUsagePercent { get; init; }

	public int? MemoryUsedPercent { get; init; }
}
