using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Devices;

/// <summary>
/// Presence for provider-registered devices. Deliberately in memory only: a provider owns whether its
/// hardware is reachable, and nothing is reachable before the provider says so again after a restart.
/// </summary>
public sealed class ProviderDevicePresenceTracker
{
	private readonly ConcurrentDictionary<Guid, bool> _online = new();

	/// <summary>Records presence and reports whether that changed it, so only real flips are published.</summary>
	public bool Set(Guid deviceId, bool online)
	{
		var wasOnline = IsOnline(deviceId);
		_online[deviceId] = online;
		return wasOnline != online;
	}

	/// <summary>Drops the device entirely. Returns whether it was online, which withdrawing it ends.</summary>
	public bool Forget(Guid deviceId) => _online.TryRemove(deviceId, out var online) && online;

	public bool IsOnline(Guid deviceId) => _online.TryGetValue(deviceId, out var online) && online;
}
