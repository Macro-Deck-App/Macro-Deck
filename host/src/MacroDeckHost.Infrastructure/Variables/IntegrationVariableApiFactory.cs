using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Variables;

public sealed class IntegrationVariableApiFactory(
	IVariableRefreshSignal? refresh = null,
	IVariablePollingInvalidationSignal? declarations = null) : IIntegrationVariableApiFactory
{
	public IVariableApi Create(string integrationId, IServiceProvider scopedServices)
	{
		return new IntegrationVariableApi(integrationId,
			scopedServices.GetRequiredService<IVariableService>(),
			refresh,
			declarations);
	}
}
