namespace MacroDeck.Sdk.Android;

/// <summary>
/// What is on the device's screen, as Macro Deck last read it. A value is <c>null</c> when the device does not
/// report it.
/// </summary>
public sealed record AndroidDeviceStatus
{
	public bool? IsScreenOn { get; init; }

	/// <summary>True while the lock screen is showing.</summary>
	public bool? IsLocked { get; init; }

	/// <summary>The package name of the app in the foreground, for example <c>com.android.chrome</c>.</summary>
	public string? ForegroundPackage { get; init; }
}
