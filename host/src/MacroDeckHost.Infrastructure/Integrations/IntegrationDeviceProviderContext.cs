using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Binds one in-process integration to the device registry. The integration id is captured here, so a
/// provider can only ever register devices in its own name.
/// </summary>
internal sealed class IntegrationDeviceProviderContext : IDeviceProviderContext
{
	private readonly string _integrationId;
	private readonly IPluginDeviceRegistry _registry;

	public IntegrationDeviceProviderContext(string integrationId, IPluginDeviceRegistry registry)
	{
		_integrationId = integrationId;
		_registry = registry;
	}

	public Task<DeviceRegistration> RegisterDeviceAsync(
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
		=> _registry.RegisterAsync(_integrationId, device, cancellationToken);

	public Task UpdateDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken = default)
		=> _registry.UpdateAsync(_integrationId, device, cancellationToken);

	public Task SetDevicePresenceAsync(
		string deviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default)
		=> _registry.SetPresenceAsync(_integrationId, deviceId, presence, cancellationToken);

	public Task UnregisterDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
		=> _registry.UnregisterAsync(_integrationId, deviceId, cancellationToken);
}
