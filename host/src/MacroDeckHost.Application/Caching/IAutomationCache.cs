using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Caching;

public interface IAutomationCache
{
	Task InitializeCache();

	AutomationEntity? GetById(Guid id);

	List<AutomationEntity> GetAll();

	Task AddOrUpdate(AutomationEntity automation);

	Task Remove(Guid id);
}
