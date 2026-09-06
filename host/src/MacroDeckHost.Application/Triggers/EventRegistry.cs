using MacroDeckHost.Application.Integrations;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Triggers;

public interface IEventRegistry
{
	IReadOnlyList<EventDefinitionDescriptor> GetDefinitions();

	EventDefinitionDescriptor? Find(string qualifiedEventId);

	object? FindProvider(string qualifiedEventId);
}

public sealed class EventRegistry : IEventRegistry
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IReadOnlyList<IHostEventProvider> _hostProviders;
	private readonly ILogger _logger;

	public EventRegistry(
		IIntegrationRegistry integrations,
		IEnumerable<IHostEventProvider> hostProviders,
		ILogger logger)
	{
		_integrations = integrations;
		_hostProviders = hostProviders.ToList();
		_logger = logger.ForContext<EventRegistry>();
	}

	public IReadOnlyList<EventDefinitionDescriptor> GetDefinitions()
	{
		var descriptors = new List<EventDefinitionDescriptor>();

		foreach (var provider in _hostProviders)
		{
			foreach (var definition in provider.EventDefinitions)
			{
				if (!QualifiedId.TryCreate(provider.ProviderId,
					definition.Id,
					OwnerIdKind.HostProvider,
					LocalIdKind.Declared,
					out var id))
				{
					_logger.Warning("Host provider {ProviderId} declared an invalid event id {EventId}; skipping it",
						provider.ProviderId,
						definition.Id);
					continue;
				}

				descriptors.Add(new EventDefinitionDescriptor(id,
					provider.ProviderId,
					provider.ProviderName,
					false,
					definition));
			}
		}

		foreach (var integration in _integrations.Integrations)
		{
			if (!_integrations.IsEnabled(integration.Id) || integration is not IEventProvider provider)
			{
				continue;
			}

			foreach (var definition in provider.EventDefinitions)
			{
				if (!QualifiedId.TryCreate(integration.Id,
					definition.Id,
					OwnerIdKind.Package,
					LocalIdKind.Declared,
					out var id))
				{
					_logger.Warning("Integration {IntegrationId} declared an invalid event id {EventId}; skipping it",
						integration.Id,
						definition.Id);
					continue;
				}

				descriptors.Add(new EventDefinitionDescriptor(id,
					integration.Id,
					ProviderDisplayName.Resolve(provider.ProviderName, integration),
					true,
					definition));
			}
		}

		return descriptors;
	}

	public object? FindProvider(string qualifiedEventId)
	{
		if (!QualifiedId.TryParse(qualifiedEventId, out var id))
		{
			return null;
		}

		var hostProvider = _hostProviders.FirstOrDefault(p => p.ProviderId == id.OwnerId);
		if (hostProvider is not null)
		{
			return hostProvider;
		}

		var integration = _integrations.Integrations.FirstOrDefault(i => i.Id == id.OwnerId);
		return integration is not null && _integrations.IsEnabled(integration.Id) && integration is IEventProvider
			? integration
			: null;
	}

	public EventDefinitionDescriptor? Find(string qualifiedEventId)
	{
		if (!QualifiedId.TryParse(qualifiedEventId, out var id))
		{
			return null;
		}

		var hostProvider = _hostProviders.FirstOrDefault(p => p.ProviderId == id.OwnerId);
		if (hostProvider is not null)
		{
			return Describe(hostProvider.ProviderId, hostProvider.ProviderName, false, hostProvider.EventDefinitions);
		}

		var integration = _integrations.Integrations.FirstOrDefault(i => i.Id == id.OwnerId);
		if (integration is null || !_integrations.IsEnabled(integration.Id) || integration is not IEventProvider ip)
		{
			return null;
		}

		return Describe(integration.Id,
			ProviderDisplayName.Resolve(ip.ProviderName, integration),
			true,
			ip.EventDefinitions);

		EventDefinitionDescriptor? Describe(
			string ownerId,
			LocalizedText providerName,
			bool isIntegration,
			IReadOnlyList<EventDefinition> definitions)
		{
			var definition = definitions.FirstOrDefault(d => d.Id == id.LocalId);
			return definition is null
				? null
				: new EventDefinitionDescriptor(id, ownerId, providerName, isIntegration, definition);
		}
	}
}
