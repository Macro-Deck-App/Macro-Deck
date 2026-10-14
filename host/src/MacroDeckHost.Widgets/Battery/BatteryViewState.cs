using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Widgets.Battery;

internal enum BatteryFaceStatus
{
	Live,
	NotConnected,
	NotAvailable
}

internal enum BatteryViewHint
{
	None,
	NoDevices,
	PickDevice
}

internal sealed record BatteryFace
{
	public required string Id { get; init; }

	public string Name { get; init; } = string.Empty;

	public string Kind { get; init; } = BatteryDeviceKinds.Other;

	public int? Level { get; init; }

	public BatteryChargeState ChargeState { get; init; }

	public BatteryFaceStatus Status { get; init; }

	public int? MinutesRemaining { get; init; }

	public int? MinutesToFull { get; init; }

	public string? Color { get; init; }

	public bool IsMuted => Status != BatteryFaceStatus.Live || Level is null;

	public bool IsCharging => Status == BatteryFaceStatus.Live && ChargeState == BatteryChargeState.Charging;

	public double Fill => Level is { } level && Status != BatteryFaceStatus.NotAvailable ? level / 100d : 0;
}

internal sealed record BatteryViewState
{
	public static readonly BatteryViewState NoDevices = new() { Hint = BatteryViewHint.NoDevices };

	public static readonly BatteryViewState PickDevice = new() { Hint = BatteryViewHint.PickDevice };

	public IReadOnlyList<BatteryFace> Faces { get; init; } = [];

	public BatteryViewHint Hint { get; init; }

	public bool Equals(BatteryViewState? other)
	{
		return other is not null && Hint == other.Hint && Faces.SequenceEqual(other.Faces);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Hint, Faces.Count);
	}
}
