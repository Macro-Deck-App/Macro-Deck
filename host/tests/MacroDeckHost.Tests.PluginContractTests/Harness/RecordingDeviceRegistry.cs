using System.Collections.Concurrent;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

/// <summary>
/// Stands in for the host's device registry, keeping the identity rule that matters to the contract:
/// a registration under a known (provider, provider-local id) is the same device, and unregistering
/// retains it and only takes it offline.
/// </summary>
internal sealed class RecordingDeviceRegistry : IPluginDeviceRegistry
{
	private readonly ConcurrentDictionary<(string Provider, string Device), string> _ids = new();
	private readonly ConcurrentDictionary<(string Provider, string Device), DeviceDescriptor> _devices = new();
	private readonly ConcurrentDictionary<(string Provider, string Device), bool> _online = new();

	public IReadOnlyDictionary<(string Provider, string Device), DeviceDescriptor> Devices => _devices;

	public bool IsOnline(string providerId, string deviceId)
		=> _online.TryGetValue((providerId, deviceId), out var online) && online;

	public string? AssignedIdOf(string providerId, string deviceId)
		=> _ids.GetValueOrDefault((providerId, deviceId));

	public Task<DeviceRegistration> RegisterAsync(
		string providerId,
		DeviceDescriptor device,
		CancellationToken cancellationToken = default)
	{
		var key = (providerId, device.Id);
		var assigned = _ids.GetOrAdd(key, static _ => Guid.NewGuid().ToString());
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
		foreach (var key in _online.Keys.Where(key =>
			string.Equals(key.Provider, providerId, StringComparison.Ordinal)))
		{
			_online[key] = false;
		}

		return Task.CompletedTask;
	}
}
