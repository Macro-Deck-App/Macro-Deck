using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Devices;

/// <summary>
/// The host side of the device provider contract: what an in-process integration or a connected plugin
/// reaches when it registers hardware. Provider identity is supplied by the caller's trusted context -
/// the integration id, or the authenticated plugin session - never by the payload.
/// </summary>
public interface IPluginDeviceRegistry
{
	/// <summary>
	/// Registers a device, reusing the persisted device previously registered under the same
	/// <paramref name="providerId" /> and descriptor id if there is one.
	/// </summary>
	Task<DeviceRegistration> RegisterAsync(
		string providerId,
		DeviceDescriptor device,
		CancellationToken cancellationToken = default);

	/// <summary>Refreshes a registered device's metadata. Unknown devices are ignored.</summary>
	Task UpdateAsync(string providerId, DeviceDescriptor device, CancellationToken cancellationToken = default);

	/// <summary>Records whether a registered device is reachable. Unknown devices are ignored.</summary>
	Task SetPresenceAsync(
		string providerId,
		string providerDeviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a device from the current session. The persisted device is retained so a later
	/// registration under the same identity is recognised as the same device.
	/// </summary>
	Task UnregisterAsync(
		string providerId,
		string providerDeviceId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws every device of one provider - what a stopping integration or a dropped plugin session
	/// leaves behind. Retains them, exactly as <see cref="UnregisterAsync" /> does.
	/// </summary>
	Task UnregisterAllAsync(string providerId, CancellationToken cancellationToken = default);
}
