using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Devices.Surfaces;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;

public sealed class RemoteDeviceProviderRegistry : IDisposable
{
	/// <summary>
	/// The <c>device-provider</c> version that introduced <c>session.open</c>. A plugin that negotiated
	/// version 1 knows only <c>describe</c> and <c>devices</c>: it must keep registering, updating and
	/// unregistering devices, and must never be sent a session operation it would have to fail. Serving
	/// no provider at all is what makes that degrade to "registration only" rather than to a fault.
	/// </summary>
	private const int SessionsMinimumVersion = 2;

	private readonly IPluginSessionRegistry _sessions;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly RemoteDeviceSessionRegistry _deviceSessions;

	private readonly ConcurrentDictionary<string, RemoteDeviceSurfaceProvider> _adapters =
		new(StringComparer.Ordinal);

	public RemoteDeviceProviderRegistry(
		IPluginSessionRegistry sessions,
		IPluginCapabilityInvoker invoker,
		RemoteDeviceSessionRegistry deviceSessions)
	{
		_sessions = sessions;
		_invoker = invoker;
		_deviceSessions = deviceSessions;
		_sessions.SessionEnded += OnSessionEnded;
	}

	public IDeviceSurfaceProvider? Resolve(string providerId)
	{
		if (_sessions.GetCapabilities(providerId) is not { } capabilities ||
			!capabilities.Capabilities.TryGetValue(CapabilityKinds.DeviceProvider, out var negotiated) ||
			!negotiated.Accepted ||
			negotiated.NegotiatedVersion is not >= SessionsMinimumVersion)
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new RemoteDeviceSurfaceProvider(id, state.Invoker, state.Sessions),
			(Invoker: _invoker, Sessions: _deviceSessions));
	}

	public void Dispose() => _sessions.SessionEnded -= OnSessionEnded;

	// An adapter caches the session ids it opened, and a reconnect negotiates its capability versions
	// afresh - an entry that outlived the session would keep addressing sessions the plugin has forgotten
	// and would keep whatever version the previous session happened to accept.
	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e) =>
		_adapters.TryRemove(e.PluginId, out _);
}
