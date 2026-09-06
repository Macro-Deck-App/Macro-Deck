using MacroDeckHost.Application.Integrations;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed class MusicPlayerRegistry : IMusicPlayerRegistry
{
	private const string DefaultLocalId = "default";

	private readonly IIntegrationRegistry _integrations;
	private readonly ILogger _logger;

	public MusicPlayerRegistry(IIntegrationRegistry integrations, ILogger logger)
	{
		_integrations = integrations;
		_logger = logger.ForContext<MusicPlayerRegistry>();
	}

	public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances()
	{
		var descriptors = new List<MusicPlayerInstanceDescriptor>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var integration in EnabledProviders())
		{
			var hasIcon = integration is IIntegrationIconProvider;
			var provider = (IMusicPlayerProvider)integration;

			foreach (var instance in provider.GetInstances())
			{
				var localId = IsSingleConfiguration(integration) ? DefaultLocalId : instance.Id;
				if (!QualifiedId.TryCreate(integration.Id, localId, out var id))
				{
					_logger.Warning(
						"Integration {IntegrationId} announced an invalid music player instance id {LocalId}; " +
						"skipping it",
						integration.Id,
						localId);
					continue;
				}

				var instanceId = id.ToString();
				if (!seen.Add(instanceId))
				{
					_logger.Warning(
						"Integration {IntegrationId} announced music player instance {LocalId} more than once; " +
						"skipping the duplicate",
						integration.Id,
						localId);
					continue;
				}

				descriptors.Add(new MusicPlayerInstanceDescriptor(instanceId,
					integration.Id,
					ProviderDisplayName.Resolve(provider.ProviderName, integration),
					instance.DisplayName,
					hasIcon));
			}
		}

		return descriptors;
	}

	public IMusicPlayer? GetPlayer(string instanceId)
	{
		if (!QualifiedId.TryParse(instanceId, out var id))
		{
			return null;
		}

		var integration = EnabledProviders().FirstOrDefault(i => i.Id == id.OwnerId);
		if (integration is not IMusicPlayerProvider provider)
		{
			return null;
		}

		if (id.LocalId == DefaultLocalId && IsSingleConfiguration(integration))
		{
			var providerInstances = provider.GetInstances();
			return providerInstances.Count > 0 ? provider.GetPlayer(providerInstances[0].Id) : null;
		}

		return provider.GetPlayer(id.LocalId);
	}

	public IMusicPlayer? DefaultPlayer
	{
		get
		{
			var instances = GetInstances();
			return instances.Count > 0 ? GetPlayer(instances[0].InstanceId) : null;
		}
	}

	private static bool IsSingleConfiguration(IIntegration integration)
		=> integration is IConfigFlowProvider { AllowsMultipleConfigurations: false };

	private IEnumerable<IIntegration> EnabledProviders() => _integrations.Integrations
		.Where(integration => integration is IMusicPlayerProvider && _integrations.IsEnabled(integration.Id));
}
