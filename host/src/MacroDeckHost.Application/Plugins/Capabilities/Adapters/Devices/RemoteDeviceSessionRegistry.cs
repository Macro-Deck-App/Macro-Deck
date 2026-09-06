using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;

/// <summary>Which plugin and device an open remote device session belongs to.</summary>
public sealed record RemoteDeviceSessionOwner(string PluginId, Guid DeviceId);

/// <summary>
/// The ownership record behind every <c>devices</c> host-api call that names a session. A session id
/// travels to the plugin over <c>session.open</c> and comes back on the plugin's own calls, so it is an
/// untrusted string on the way back: without this lookup a plugin could name another plugin's session
/// and drive a device it does not provide.
/// </summary>
public sealed class RemoteDeviceSessionRegistry
{
	private readonly ConcurrentDictionary<string, RemoteDeviceSessionOwner> _sessions =
		new(StringComparer.Ordinal);

	public void Add(string sessionId, string pluginId, Guid deviceId)
		=> _sessions[sessionId] = new RemoteDeviceSessionOwner(pluginId, deviceId);

	public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

	/// <summary>Resolves the device a session addresses, but only for the plugin that owns it. Null for
	/// an unknown session and for one owned by any other plugin - the two are deliberately
	/// indistinguishable to the caller.</summary>
	public Guid? ResolveDevice(string pluginId, string? sessionId)
		=> sessionId is not null &&
			_sessions.TryGetValue(sessionId, out var owner) &&
			string.Equals(owner.PluginId, pluginId, StringComparison.Ordinal)
				? owner.DeviceId
				: null;
}
