using System.Collections.Concurrent;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces.InProcess;

public sealed class DeviceSurfaceProviderRegistry : IDisposable
{
	private readonly IIntegrationRegistry _integrations;
	private readonly Func<IDeviceSurfaceService> _service;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, InProcessDeviceSurfaceProvider> _adapters =
		new(StringComparer.Ordinal);

	public DeviceSurfaceProviderRegistry(
		IIntegrationRegistry integrations,
		Func<IDeviceSurfaceService> service,
		ILogger logger)
	{
		_integrations = integrations;
		_service = service;
		_logger = logger;
		_integrations.AvailabilityChanged += OnAvailabilityChanged;
	}

	public IDeviceSurfaceProvider? Resolve(string providerId)
	{
		var integration = _integrations.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, providerId, StringComparison.Ordinal));

		// A remote plugin is registered as an integration too, so without this exclusion every plugin
		// would match here as well as in the remote registry. A plugin's provider lives in its own
		// process and is served over the plugin transport or not at all.
		if (integration is not IDeviceProvider provider ||
			integration is RemotePluginIntegration ||
			!_integrations.IsEnabled(providerId))
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessDeviceSurfaceProvider(id, state.Provider, state.Service, state.Logger),
			(Provider: provider, Service: _service, Logger: _logger));
	}

	public void Dispose()
	{
		_integrations.AvailabilityChanged -= OnAvailabilityChanged;
		_adapters.Clear();
	}

	// An adapter caches the IDeviceProvider instance it was built for; disabling and re-enabling an
	// integration re-instantiates it, so an entry that outlived the change would keep serving sessions
	// from the object the host has already thrown away.
	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e)
		=> _adapters.TryRemove(e.IntegrationId, out _);
}

/// <summary>
/// The one place the two kinds of provider are told apart. An in-process integration is served by the
/// in-process adapter and a connected plugin by the remote one; the two never overlap, because the
/// in-process registry excludes plugin-backed integrations by construction.
/// </summary>
public sealed class DeviceSurfaceProviderResolver : IDeviceSurfaceProviderResolver
{
	private readonly DeviceSurfaceProviderRegistry _inProcess;
	private readonly RemoteDeviceProviderRegistry _remote;

	public DeviceSurfaceProviderResolver(DeviceSurfaceProviderRegistry inProcess, RemoteDeviceProviderRegistry remote)
	{
		_inProcess = inProcess;
		_remote = remote;
	}

	public IDeviceSurfaceProvider? Resolve(string providerId)
		=> _inProcess.Resolve(providerId) ?? _remote.Resolve(providerId);
}
