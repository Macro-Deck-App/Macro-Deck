using System.Collections.Concurrent;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>
/// Resolves a built-in integration's id to whichever <see cref="IBuiltInIntegrationUiProvider" /> serves
/// its surfaces. Consulted after <see cref="UiProviderRegistry" />, so an integration that does implement
/// <c>IUiProvider</c> itself keeps serving its own surfaces and this never shadows it.
/// </summary>
public sealed class IntegrationUiProviderRegistry
{
	private readonly IEnumerable<IBuiltInIntegrationUiProvider> _providers;
	private readonly Func<IUiSessionSink> _sink;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, InProcessUiSessionProvider> _adapters = new(StringComparer.Ordinal);

	public IntegrationUiProviderRegistry(
		IEnumerable<IBuiltInIntegrationUiProvider> providers,
		Func<IUiSessionSink> sink,
		ILogger logger)
	{
		_providers = providers;
		_sink = sink;
		_logger = logger;
	}

	public IUiSessionProvider? Resolve(string providerId)
	{
		var provider = _providers.FirstOrDefault(candidate =>
			string.Equals(candidate.IntegrationId, providerId, StringComparison.Ordinal));

		if (provider is null)
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id, state.Provider, state.Sink(), state.Logger),
			(Provider: provider, Sink: _sink, Logger: _logger));
	}
}
