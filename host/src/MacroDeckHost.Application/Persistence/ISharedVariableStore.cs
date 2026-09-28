using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence;

public interface ISharedVariableStore
{
	bool TryLoad(out IReadOnlyList<SharedVariable> entries);

	bool Save(IEnumerable<SharedVariable> entries);
}
