using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;

namespace MacroDeckHost.Infrastructure.Integrations;

// The integration id is captured here, so a provider can only ever publish devices in its own name.
internal sealed class IntegrationBatteryProviderContext : IBatteryProviderContext
{
	private readonly string _integrationId;
	private readonly IBatteryRegistry _registry;

	public IntegrationBatteryProviderContext(string integrationId, IBatteryRegistry registry)
	{
		_integrationId = integrationId;
		_registry = registry;
	}

	public Task PublishAsync(BatteryDevice device, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);
		return PublishAsync([device], cancellationToken);
	}

	public Task PublishAsync(IReadOnlyList<BatteryDevice> devices, CancellationToken cancellationToken = default)
	{
		_registry.Publish(_integrationId, null, devices);
		return Task.CompletedTask;
	}

	public Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
		_registry.Remove(_integrationId, [deviceId]);
		return Task.CompletedTask;
	}
}
