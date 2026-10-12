using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableOwnerNames
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IServiceScopeFactory _scopeFactory;

	public VariableOwnerNames(IIntegrationRegistry integrations, IServiceScopeFactory scopeFactory)
	{
		_integrations = integrations;
		_scopeFactory = scopeFactory;
	}

	public async Task<string> Resolve(string? integrationId)
	{
		if (string.IsNullOrEmpty(integrationId))
		{
			return string.Empty;
		}

		var integration = _integrations.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, integrationId, StringComparison.Ordinal));
		if (integration is null || integration.Name.IsEmpty)
		{
			return integrationId;
		}

		var name = await ActiveLocalization.Resolve(_scopeFactory, integration.Name);
		return string.IsNullOrWhiteSpace(name) ? integrationId : name;
	}
}
