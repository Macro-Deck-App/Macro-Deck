using MacroDeck.Sdk;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Integrations;

public static class IntegrationCapabilityValidator
{
	public static IReadOnlyList<CapabilityIdConflict> Validate(IIntegration integration)
	{
		var conflicts = new List<CapabilityIdConflict>();

		var ownerConflict = DeclaredIdValidator.ValidateOwner(integration.Id);
		if (ownerConflict is not null)
		{
			conflicts.Add(ownerConflict);
			return conflicts;
		}

		// Not filtered by IActionDefinition.Platforms/RunsHere(): an action id must be unique on every
		// platform, not just the one running now, so a platform-restricted action still counts here.
		DeclaredIdValidator.Validate(conflicts,
			integration.Id,
			"Action",
			integration.Actions.Select(action => action.Id));

		if (integration is IEventProvider eventProvider)
		{
			DeclaredIdValidator.Validate(conflicts,
				integration.Id,
				"Event",
				eventProvider.EventDefinitions.Select(definition => definition.Id));
		}

		if (integration is IVariableProvider variableProvider)
		{
			DeclaredIdValidator.Validate(conflicts,
				integration.Id,
				"Variable",
				DeclaredVariableDefinitionIds(variableProvider));
		}

		return conflicts;
	}

	private static IEnumerable<string> DeclaredVariableDefinitionIds(IVariableProvider provider)
		=> provider.DeclaredVariables
			.Where(definition => !VariableNameTemplate.IsTemplate(definition.Name))
			.Select(definition => definition.ResolvedId)
			.OfType<string>();
}
