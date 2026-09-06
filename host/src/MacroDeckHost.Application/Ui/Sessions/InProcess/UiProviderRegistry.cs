using System.Collections.Concurrent;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

public sealed class UiProviderRegistry : IDisposable
{
	private readonly IIntegrationRegistry _integrations;
	private readonly Func<IUiSessionSink> _sink;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, InProcessUiSessionProvider> _adapters =
		new(StringComparer.Ordinal);

	public UiProviderRegistry(IIntegrationRegistry integrations, Func<IUiSessionSink> sink, ILogger logger)
	{
		_integrations = integrations;
		_sink = sink;
		_logger = logger;
		_integrations.AvailabilityChanged += OnAvailabilityChanged;
	}

	public IUiSessionProvider? Resolve(string providerId)
	{
		var integration = _integrations.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, providerId, StringComparison.Ordinal));

		// A remote plugin is registered as an integration too, so without this exclusion every plugin
		// would match here as well as in the remote registry and the resolver would have two candidates
		// for one provider id. A plugin has no UiView and no synchronous BuildTree - it is served by
		// RemoteUiSessionProvider or not at all.
		if (integration is not IUiProvider provider ||
			integration is RemotePluginIntegration ||
			!_integrations.IsEnabled(providerId))
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id, state.Provider, state.Sink(), state.Logger),
			(Provider: provider, Sink: _sink, Logger: _logger));
	}

	public void Dispose()
	{
		_integrations.AvailabilityChanged -= OnAvailabilityChanged;

		foreach (var providerId in _adapters.Keys)
		{
			Evict(providerId);
		}
	}

	// An adapter caches the IUiProvider instance it was built for. Disabling and re-enabling an
	// integration re-instantiates it, so an entry that outlived the availability change would keep
	// serving sessions from the object the host has already thrown away.
	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e)
		=> Evict(e.IntegrationId);

	private void Evict(string integrationId)
	{
		if (_adapters.TryRemove(integrationId, out var adapter))
		{
			_ = DisposeAdapterAsync(adapter, integrationId);
		}
	}

	private async Task DisposeAdapterAsync(InProcessUiSessionProvider adapter, string integrationId)
	{
		try
		{
			await adapter.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Failed to dispose the UI provider adapter for integration '{IntegrationId}'",
				integrationId);
		}
	}
}
