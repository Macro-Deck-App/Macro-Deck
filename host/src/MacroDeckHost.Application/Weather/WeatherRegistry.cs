using MacroDeckHost.Application.Integrations;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Weather;
using Serilog;

namespace MacroDeckHost.Application.Weather;

public sealed class WeatherRegistry : IWeatherRegistry
{
	private readonly IIntegrationRegistry _integrations;
	private readonly ILogger _logger;

	public WeatherRegistry(IIntegrationRegistry integrations, ILogger logger)
	{
		_integrations = integrations;
		_logger = logger.ForContext<WeatherRegistry>();
	}

	public IReadOnlyList<WeatherStationDescriptor> GetInstances()
	{
		var descriptors = new List<WeatherStationDescriptor>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var integration in EnabledProviders())
		{
			var hasIcon = integration is IIntegrationIconProvider;
			var provider = (IWeatherProvider)integration;

			foreach (var instance in provider.GetInstances())
			{
				if (!QualifiedId.TryCreate(integration.Id, instance.Id, out var id))
				{
					_logger.Warning("Integration {IntegrationId} announced an invalid weather station id {LocalId}; " +
						"skipping it",
						integration.Id,
						instance.Id);
					continue;
				}

				var instanceId = id.ToString();
				if (!seen.Add(instanceId))
				{
					_logger.Warning("Integration {IntegrationId} announced weather station {LocalId} more than once; " +
						"skipping the duplicate",
						integration.Id,
						instance.Id);
					continue;
				}

				descriptors.Add(new WeatherStationDescriptor(instanceId,
					integration.Id,
					ProviderDisplayName.Resolve(provider.ProviderName, integration),
					instance.DisplayName,
					hasIcon));
			}
		}

		return descriptors;
	}

	public IWeatherStation? GetStation(string instanceId)
	{
		if (!QualifiedId.TryParse(instanceId, out var id))
		{
			return null;
		}

		var integration = EnabledProviders().FirstOrDefault(i => i.Id == id.OwnerId);
		return integration is IWeatherProvider provider ? provider.GetStation(id.LocalId) : null;
	}

	public IWeatherStation? DefaultStation
	{
		get
		{
			var instances = GetInstances();
			return instances.Count > 0 ? GetStation(instances[0].InstanceId) : null;
		}
	}

	private IEnumerable<IIntegration> EnabledProviders() => _integrations.Integrations
		.Where(integration => integration is IWeatherProvider && _integrations.IsEnabled(integration.Id));
}
