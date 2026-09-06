using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeck.Sdk;

namespace MacroDeckHost.Infrastructure.Integrations;

internal static class IntegrationGatewayBinder
{
	public static void Bind(IIntegration integration, IAdbGateway adbGateway, IVariableBindingStore bindingStore)
	{
		if (integration is IAdbGatewayConsumer consumer)
		{
			consumer.UseGateway(adbGateway);
		}

		if (integration is IHomeAssistantBindingStoreConsumer bindingStoreConsumer)
		{
			bindingStoreConsumer.UseBindingStore(bindingStore);
		}
	}
}
