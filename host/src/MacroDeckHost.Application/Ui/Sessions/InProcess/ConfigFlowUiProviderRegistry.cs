using System.Collections.Concurrent;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>
/// Serves an in-process, started config flow's <see cref="IUiConfigFlow" /> as a UI session provider,
/// under the synthetic provider id <see cref="ProviderIdFor" /> mints for it.
/// <c>ConfigFlowManager</c> registers a flow here the moment it starts and unregisters it the moment it
/// ends - complete or abandoned - so a provider id can never outlive the flow instance it names.
/// </summary>
public sealed class ConfigFlowUiProviderRegistry
{
	private readonly Func<IUiSessionSink> _sink;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, IUiConfigFlow> _flows = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, InProcessUiSessionProvider> _adapters = new(StringComparer.Ordinal);

	public ConfigFlowUiProviderRegistry(Func<IUiSessionSink> sink, ILogger logger)
	{
		_sink = sink;
		_logger = logger;
	}

	public static string ProviderIdFor(Guid flowId) => $"config-flow:{flowId:N}";

	public void Register(Guid flowId, IUiConfigFlow flow) => _flows[ProviderIdFor(flowId)] = flow;

	public void Unregister(Guid flowId)
	{
		var providerId = ProviderIdFor(flowId);
		_flows.TryRemove(providerId, out _);

		if (_adapters.TryRemove(providerId, out var adapter))
		{
			_ = DisposeAdapterAsync(adapter, providerId);
		}
	}

	public IUiSessionProvider? Resolve(string providerId)
	{
		if (!_flows.TryGetValue(providerId, out var flow))
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id,
				new ConfigFlowUiProvider(state.Flow),
				state.Sink(),
				state.Logger),
			(Flow: flow, Sink: _sink, Logger: _logger));
	}

	private async Task DisposeAdapterAsync(InProcessUiSessionProvider adapter, string providerId)
	{
		try
		{
			await adapter.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Failed to dispose the config flow UI provider adapter '{ProviderId}'",
				providerId);
		}
	}

	private sealed class ConfigFlowUiProvider(IUiConfigFlow flow) : IUiProvider
	{
		public IReadOnlyList<UiSurfaceDeclaration> Surfaces => [];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
			=> flow.CreateUiSessionAsync(request, cancellationToken);
	}
}
