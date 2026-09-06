using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>
/// Records what a provider asked the host to do with its devices, keeping the host's identity rule:
/// the same (provider, provider-local id) is the same device, and unregistering retains it and only
/// takes it offline.
/// </summary>
internal sealed class FakePluginDeviceRegistry : IPluginDeviceRegistry
{
	private readonly Dictionary<(string Provider, string Device), string> _ids = [];
	private readonly Dictionary<(string Provider, string Device), DeviceDescriptor> _devices = [];
	private readonly Dictionary<(string Provider, string Device), bool> _online = [];

	public IReadOnlyDictionary<(string Provider, string Device), DeviceDescriptor> Devices => _devices;

	public bool IsOnline(string providerId, string deviceId)
		=> _online.GetValueOrDefault((providerId, deviceId));

	public string? AssignedIdOf(string providerId, string deviceId)
		=> _ids.GetValueOrDefault((providerId, deviceId));

	public Task<DeviceRegistration> RegisterAsync(
		string providerId,
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
	{
		var key = (providerId, device.Id);
		if (!_ids.TryGetValue(key, out var assigned))
		{
			assigned = Guid.NewGuid().ToString();
			_ids[key] = assigned;
		}

		_devices[key] = device;
		_online[key] = device.Presence == DevicePresence.Online;

		return Task.FromResult(new DeviceRegistration(assigned, device.Id));
	}

	public Task UpdateAsync(string providerId, DeviceDescriptor device, CancellationToken cancellationToken = default)
	{
		var key = (providerId, device.Id);
		if (_devices.ContainsKey(key))
		{
			_devices[key] = device;
			_online[key] = device.Presence == DevicePresence.Online;
		}

		return Task.CompletedTask;
	}

	public Task SetPresenceAsync(
		string providerId,
		string providerDeviceId,
		DevicePresence presence,
		CancellationToken cancellationToken = default)
	{
		var key = (providerId, providerDeviceId);
		if (_devices.ContainsKey(key))
		{
			_online[key] = presence == DevicePresence.Online;
		}

		return Task.CompletedTask;
	}

	public Task UnregisterAsync(
		string providerId,
		string providerDeviceId,
		CancellationToken cancellationToken = default)
	{
		_online[(providerId, providerDeviceId)] = false;
		return Task.CompletedTask;
	}

	public Task UnregisterAllAsync(string providerId, CancellationToken cancellationToken = default)
	{
		foreach (var key in _online.Keys.Where(key => key.Provider == providerId).ToArray())
		{
			_online[key] = false;
		}

		return Task.CompletedTask;
	}
}
