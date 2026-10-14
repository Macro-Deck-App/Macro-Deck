using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Variables;

public static class HostPublishedIntegrationVariables
{
	public static IReadOnlyList<VariableEntity> Of(
		VariableRegistry registry,
		string integrationId,
		IReadOnlySet<string> declaredNames)
	{
		return
		[
			.. registry.GetByOwnerIntegration(integrationId)
				.Where(variable => variable.PublishedByHost && !declaredNames.Contains(variable.Name))
				.OrderBy(variable => variable.Name, StringComparer.Ordinal)
		];
	}
}
