using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IIntegrationConfigEntryRepository
{
	Task<IReadOnlyList<IntegrationConfigEntryEntity>> GetByIntegrationId(string integrationId);

	Task<IntegrationConfigEntryEntity?> GetById(Guid id);

	Task Create(IntegrationConfigEntryEntity entry);

	Task Update(IntegrationConfigEntryEntity entry);

	Task TryDeleteById(Guid id);
}
