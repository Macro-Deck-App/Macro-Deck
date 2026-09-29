using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Application.Integrations;

public static class ConfigurationRequirement
{
	public static bool RequiresConfiguration(this IIntegration integration)
		=> integration is IConfigFlowProvider { RequiresConfiguration: true };
}
