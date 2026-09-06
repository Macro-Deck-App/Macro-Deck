using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Integrations;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>
/// Serves one configured instance of an in-process action's <see cref="IUiConfigurableActionDefinition" />
/// as a UI session provider, under the synthetic provider id <see cref="ProviderIdFor" /> mints for it.
/// Stateless - the action instance being configured is resolved fresh from
/// <see cref="IIntegrationRegistry" /> on every open, so there is nothing to register or leak between
/// configuration sessions.
/// </summary>
public sealed class ActionConfigUiProviderRegistry
{
	private readonly IIntegrationRegistry _integrations;
	private readonly Func<IUiSessionSink> _sink;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, InProcessUiSessionProvider> _adapters = new(StringComparer.Ordinal);

	public ActionConfigUiProviderRegistry(IIntegrationRegistry integrations, Func<IUiSessionSink> sink, ILogger logger)
	{
		_integrations = integrations;
		_sink = sink;
		_logger = logger;
	}

	public static string ProviderIdFor(string integrationId, string actionId) =>
		$"action-config:{integrationId}:{actionId}";

	public IUiSessionProvider? Resolve(string providerId)
	{
		if (!TryParse(providerId, out var integrationId, out var actionId))
		{
			return null;
		}

		if (_integrations.FindAction(integrationId, actionId) is not IUiConfigurableActionDefinition configurable)
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id,
				new ActionConfigUiProvider(state.Configurable),
				state.Sink(),
				state.Logger),
			(Configurable: configurable, Sink: _sink, Logger: _logger));
	}

	private static bool TryParse(string providerId, out string integrationId, out string actionId)
	{
		integrationId = string.Empty;
		actionId = string.Empty;

		if (!providerId.StartsWith("action-config:", StringComparison.Ordinal))
		{
			return false;
		}

		var rest = providerId["action-config:".Length..];
		var separator = rest.IndexOf(':');
		if (separator < 0)
		{
			return false;
		}

		integrationId = rest[..separator];
		actionId = rest[(separator + 1)..];
		return integrationId.Length > 0 && actionId.Length > 0;
	}

	private sealed class ActionConfigUiProvider(IUiConfigurableActionDefinition configurable) : IUiProvider
	{
		public IReadOnlyList<UiSurfaceDeclaration> Surfaces => [];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			var configurationRequest = new ActionConfigurationRequest
			{
				Session = request, Parameters = ReadParameters(request.Surface.Attributes)
			};

			return configurable.CreateConfigurationSessionAsync(configurationRequest, cancellationToken);
		}

		private static Dictionary<string, JsonElement> ReadParameters(
			IReadOnlyDictionary<string, JsonElement> attributes)
		{
			if (!attributes.TryGetValue(UiConfigSurfaceAttributes.Parameters, out var element) ||
				element.ValueKind != JsonValueKind.Object)
			{
				return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
			}

			var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
			{
				map[property.Name] = property.Value;
			}

			return map;
		}
	}
}
