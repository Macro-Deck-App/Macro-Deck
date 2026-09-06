namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The host surface a device provider registers against. Handed to
/// <see cref="IDeviceProvider.InitializeAsync" /> and safe to retain until the matching
/// <see cref="IDeviceProvider.ShutdownAsync" /> returns.
/// </summary>
public interface IDeviceProviderContext
{
	/// <summary>
	/// Registers a device, or re-registers one the host already knows under the same provider-local id.
	/// Re-registering reuses the existing device: its global id, the name the user gave it and its
	/// startup-profile assignment are preserved, and the descriptor refreshes the rest.
	/// </summary>
	/// <returns>The host-assigned identity for the device.</returns>
	/// <exception cref="ArgumentException">The descriptor's id or name is empty.</exception>
	Task<DeviceRegistration> RegisterDeviceAsync(
		DeviceDescriptor device,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Refreshes the metadata of an already registered device. Unknown devices are ignored, so a provider
	/// that raced a shutdown does not have to guard the call.
	/// </summary>
	Task UpdateDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken = default);

	/// <summary>Reports whether a registered device is currently reachable.</summary>
	Task SetDevicePresenceAsync(
		string deviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a device from this session: it goes offline and stops being offered by this provider.
	/// The device itself is retained, so reconnecting hardware re-registers as the same device rather
	/// than as a new one. Removing a device for good is the user's decision, taken in Macro Deck.
	/// </summary>
	/// <param name="deviceId">The provider-local id the device was registered under.</param>
	Task UnregisterDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}
