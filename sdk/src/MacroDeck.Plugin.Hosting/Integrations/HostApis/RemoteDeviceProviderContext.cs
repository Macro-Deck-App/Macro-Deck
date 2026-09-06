using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IDeviceProviderContext" /> over <c>host.invoke</c> against
/// <see cref="HostApis.Devices" />. Every call is a real round trip: a registration is what makes the
/// device exist for the host, so there is nothing meaningful to serve from a cache.
/// </summary>
internal sealed class RemoteDeviceProviderContext(IHostInvoker invoker) : IDeviceProviderContext
{
	public async Task<DeviceRegistration> RegisterDeviceAsync(
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);

		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
			HostOperations.Devices.Register,
			new DevicesRegisterArguments { Device = DeviceDescriptorMapper.ToDto(device) },
			cancellationToken);

		var registered = result?.Deserialize<DevicesRegisterResult>(PluginProtocolJson.Options);

		return registered is null
			? new DeviceRegistration(string.Empty, device.Id)
			: new DeviceRegistration(registered.DeviceId, registered.ProviderDeviceId);
	}

	public Task UpdateDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);

		return invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
			HostOperations.Devices.Update,
			new DevicesRegisterArguments { Device = DeviceDescriptorMapper.ToDto(device) },
			cancellationToken);
	}

	public Task SetDevicePresenceAsync(
		string deviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
			HostOperations.Devices.Presence,
			new DevicesPresenceArguments { DeviceId = deviceId, Presence = presence.ToString() },
			cancellationToken);

	public Task UnregisterDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
			HostOperations.Devices.Unregister,
			new DevicesUnregisterArguments { DeviceId = deviceId },
			cancellationToken);
}
