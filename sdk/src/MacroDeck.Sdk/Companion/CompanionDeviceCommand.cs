namespace MacroDeck.Sdk.Companion;

/// <summary>A command for a Companion device. Create one with the static members.</summary>
public sealed record CompanionDeviceCommand
{
	private CompanionDeviceCommand(
		CompanionDeviceCommandKind kind,
		int? brightnessPercent = null,
		string? orientation = null)
	{
		Kind = kind;
		BrightnessPercent = brightnessPercent;
		Orientation = orientation;
	}

	public CompanionDeviceCommandKind Kind { get; }

	/// <summary>The brightness from 0 to 100 for <see cref="CompanionDeviceCommandKind.SetBrightness" />.</summary>
	public int? BrightnessPercent { get; }

	/// <summary>One of <see cref="CompanionOrientations" /> for <see cref="CompanionDeviceCommandKind.SetOrientation" />.</summary>
	public string? Orientation { get; }

	public static CompanionDeviceCommand Vibrate { get; } = new(CompanionDeviceCommandKind.Vibrate);

	public static CompanionDeviceCommand ScreenOn { get; } = new(CompanionDeviceCommandKind.ScreenOn);

	public static CompanionDeviceCommand ScreenOff { get; } = new(CompanionDeviceCommandKind.ScreenOff);

	public static CompanionDeviceCommand Focus { get; } = new(CompanionDeviceCommandKind.Focus);

	/// <exception cref="ArgumentOutOfRangeException"><paramref name="percent" /> is not between 0 and 100.</exception>
	public static CompanionDeviceCommand SetBrightness(int percent)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(percent);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
		return new CompanionDeviceCommand(CompanionDeviceCommandKind.SetBrightness, percent);
	}

	/// <exception cref="ArgumentException"><paramref name="orientation" /> is not one of <see cref="CompanionOrientations" />.</exception>
	public static CompanionDeviceCommand SetOrientation(string orientation)
	{
		if (orientation is not (CompanionOrientations.Automatic
			or CompanionOrientations.Portrait
			or CompanionOrientations.Landscape))
		{
			throw new ArgumentException($"Unknown orientation '{orientation}'.", nameof(orientation));
		}

		return new CompanionDeviceCommand(CompanionDeviceCommandKind.SetOrientation, orientation: orientation);
	}
}
