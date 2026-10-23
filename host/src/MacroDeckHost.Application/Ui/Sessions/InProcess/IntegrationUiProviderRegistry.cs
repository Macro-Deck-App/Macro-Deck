using System.Collections.Concurrent;
using MacroDeck.Sdk.Ui;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>
/// Resolves a built-in integration's id to the <see cref="IBuiltInIntegrationUiProvider" />s that serve
/// its surfaces, asked in registration order until one serves the surface. Consulted after
/// <see cref="UiProviderRegistry" />, so an integration that does implement <c>IUiProvider</c> itself keeps
/// serving its own surfaces and this never shadows it.
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
		var providers = _providers
			.Where(candidate => string.Equals(candidate.IntegrationId, providerId, StringComparison.Ordinal))
			.ToArray();

		if (providers.Length == 0)
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id,
				new IntegrationUiProviders(state.Providers),
				state.Sink(),
				state.Logger),
			(Providers: providers, Sink: _sink, Logger: _logger));
	}

	private sealed class IntegrationUiProviders : IUiProvider
	{
		private readonly IBuiltInIntegrationUiProvider[] _providers;

		public IntegrationUiProviders(IBuiltInIntegrationUiProvider[] providers)
		{
			_providers = providers;
			Surfaces = [.. providers.SelectMany(provider => provider.Surfaces)];
		}

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; }

		public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			foreach (var provider in _providers)
			{
				var session = await provider.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
				if (session is not null)
				{
					return session;
				}
			}

			return null;
		}
	}
}
