using MacroDeckHost.Application.Persistence;

namespace MacroDeckHost.Integrations.HomeAssistant;

/// <summary>
/// Lets a host-owned caller hand <see cref="HomeAssistantIntegration"/> the
/// <see cref="IVariableBindingStore"/> singleton before <c>InitializeAsync</c> runs, the same way
/// <c>IAdbGatewayConsumer</c> gets the ADB gateway. Needed because integrations are constructed with a
/// parameterless constructor (see <see cref="IntegrationDiscovery.DiscoverIntegrations"/>) and
/// <c>IIntegrationContext</c> does not carry the binding store, so there is no other seam through which
/// <see cref="HomeAssistantWatchedEntityMigration"/> can reach it.
/// </summary>
public interface IHomeAssistantBindingStoreConsumer
{
	void UseBindingStore(IVariableBindingStore store);
}
