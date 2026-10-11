namespace MacroDeck.Sdk.Batteries;

/// <summary>
/// The host surface a battery provider publishes through. Handed to
/// <see cref="IBatteryProvider.InitializeAsync" /> and safe to retain until the matching
/// <see cref="IBatteryProvider.ShutdownAsync" /> returns. Devices are always published under the
/// calling plugin or integration; nothing in a device names its owner.
/// </summary>
public interface IBatteryProviderContext
{
	/// <summary>Adds the device, or replaces the one published under the same <see cref="BatteryDevice.Id" />.</summary>
	/// <exception cref="InvalidOperationException">The provider would have more than <see cref="BatteryLimits.MaxDevicesPerProvider" /> devices.</exception>
	Task PublishAsync(BatteryDevice device, CancellationToken cancellationToken = default);

	/// <summary>
	/// Adds or replaces several devices in one call. All or nothing: when any of them is refused, none is
	/// applied. Publishing an empty list does nothing.
	/// </summary>
	/// <exception cref="ArgumentException">The list contains <c>null</c> or the same id twice.</exception>
	/// <exception cref="InvalidOperationException">The provider would have more than <see cref="BatteryLimits.MaxDevicesPerProvider" /> devices.</exception>
	Task PublishAsync(IReadOnlyList<BatteryDevice> devices, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a device for good: readers are told it is gone, not merely unavailable. A device that is only
	/// out of reach should stay published with <see cref="BatteryDevice.IsConnected" /> <c>false</c> instead.
	/// Removing an unknown id does nothing.
	/// </summary>
	Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default);
}
