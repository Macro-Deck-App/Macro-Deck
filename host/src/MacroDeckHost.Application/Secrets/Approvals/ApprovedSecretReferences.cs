using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Secrets.Approvals;

public static class ApprovedSecretReferences
{
	public static IEnumerable<Guid> Collect(
		ISecretApprovals approvals,
		Func<string, string, IActionDefinition?> findAction,
		FlowOwner owner,
		IReadOnlyList<FlowBlockSnapshot> blocks)
	{
		foreach (var block in blocks)
		{
			if (block.Content is not { IntegrationId: { } integrationId, ActionId: { } actionId }
				|| findAction(integrationId, actionId) is not { } action
				|| !approvals.IsApproved(owner, block.Id, block.Content))
			{
				continue;
			}

			var declared = DeclaredSecretWalker.Index(action.Parameters);
			foreach (var parameter in block.Content.Parameters)
			{
				if (!declared.TryGetValue(parameter.Name, out var definition))
				{
					continue;
				}

				foreach (var id in DeclaredSecretWalker.SecretIds(definition, parameter.Value))
				{
					yield return id;
				}
			}
		}
	}
}
