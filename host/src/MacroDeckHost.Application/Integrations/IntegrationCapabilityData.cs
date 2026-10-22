using MacroDeck.Sdk;

namespace MacroDeckHost.Application.Integrations;

public static class IntegrationCapabilityData
{
	public static IIntegration? Find(IIntegrationRegistry integrations, string integrationId)
	{
		var integration = integrations.Integrations.FirstOrDefault(candidate => candidate.Id == integrationId);
		return integration is null or ISystemIntegration ? null : integration;
	}

	public static bool IsAvailable(IIntegrationRegistry integrations, IIntegration integration)
	{
		return integrations.IsEnabled(integration.Id) && integration.IsInitialized;
	}
}
