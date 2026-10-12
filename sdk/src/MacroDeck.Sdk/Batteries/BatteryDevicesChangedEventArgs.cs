namespace MacroDeck.Sdk.Batteries;

/// <summary>Raised by <see cref="IBatteryApi.Changed" /> after <see cref="IBatteryApi.Devices" /> changed.</summary>
public sealed class BatteryDevicesChangedEventArgs(IReadOnlyList<string> removedIds) : EventArgs
{
	/// <summary>
	/// Qualified ids of the devices their provider removed for good in this change. A device that left
	/// <see cref="IBatteryApi.Devices" /> without being listed here was withdrawn instead - its provider
	/// stopped, was turned off or went away - and may come back under the same id. A reader that was not
	/// connected when a removal happened may not see it here; the device list itself is always current.
	/// </summary>
	public IReadOnlyList<string> RemovedIds { get; } = removedIds ?? [];
}
