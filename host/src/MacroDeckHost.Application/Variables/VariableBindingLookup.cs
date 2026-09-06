using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Variables;

// Singleton-safe half of IVariableBindingService: resolving a variable back to its binding needs
// only the registry and the store, both singletons, so the broadcast path can do it without taking a
// dependency on the scoped service (which pulls in IVariableService and cannot be captured by one).
public sealed class VariableBindingLookup
{
	private readonly VariableRegistry _registry;
	private readonly IVariableBindingStore _bindingStore;

	public VariableBindingLookup(VariableRegistry registry, IVariableBindingStore bindingStore)
	{
		_registry = registry;
		_bindingStore = bindingStore;
	}

	/// <summary>How many of an integration's catalog resources are bound right now.</summary>
	public int CountFor(string integrationId)
		=> _bindingStore.Load().Count(b => string.Equals(b.IntegrationId, integrationId, StringComparison.Ordinal));

	public VariableBinding? FindByVariableId(Guid variableId)
	{
		var entity = _registry.GetById(variableId);
		if (entity is null || entity.OwnerIntegrationId is null || entity.DefinitionId is null)
		{
			return null;
		}

		return _bindingStore.Load()
			.FirstOrDefault(b =>
				string.Equals(b.IntegrationId, entity.OwnerIntegrationId, StringComparison.Ordinal) &&
				string.Equals(b.LocalResourceId, entity.DefinitionId, StringComparison.Ordinal));
	}
}
