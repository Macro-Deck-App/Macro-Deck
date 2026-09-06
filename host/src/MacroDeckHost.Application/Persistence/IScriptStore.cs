using MacroDeckHost.Application.Persistence.Scripts;

namespace MacroDeckHost.Application.Persistence;

public interface IScriptStore
{
	IReadOnlyList<ScriptFile> LoadAll();

	void Save(ScriptFile script);

	void Delete(Guid id);
}
