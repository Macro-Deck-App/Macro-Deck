using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Caching;

public interface IScriptCache
{
	Task InitializeCache();

	ScriptEntity? GetById(Guid id);

	List<ScriptEntity> GetAll();

	Task AddOrUpdate(ScriptEntity script);

	Task Remove(Guid id);
}
