using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence;

public interface IUserVariableStore
{
	IReadOnlyList<VariableEntity> Load();

	void Save(IEnumerable<VariableEntity> userVariables);
}
