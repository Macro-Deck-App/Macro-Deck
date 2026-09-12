using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeck.Sdk;

namespace MacroDeckHost.Infrastructure.Integrations;

internal static class IntegrationGatewayBinder
{
	public static void Bind(IIntegration integration,
		IAdbGateway adbGateway,
		ICompanionGateway companionGateway,
		IVariableBindingStore bindingStore,
		IVariableRefreshSignal refreshSignal)
	{
		if (integration is IAdbGatewayConsumer consumer)
		{
			consumer.UseGateway(adbGateway);
		}

		if (integration is ICompanionGatewayConsumer companionConsumer)
		{
			companionConsumer.UseGateway(companionGateway);
		}

		if (integration is IHomeAssistantBindingStoreConsumer bindingStoreConsumer)
		{
			bindingStoreConsumer.UseBindingStore(bindingStore);
		}

		if (integration is IVariableRefreshSignalConsumer refreshConsumer)
		{
			refreshConsumer.UseVariableRefreshSignal(refreshSignal);
		}
	}
}
