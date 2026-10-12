using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

public interface IIntegrationVariableApiFactory
{
	IVariableApi Create(string integrationId, IServiceProvider scopedServices);
}
