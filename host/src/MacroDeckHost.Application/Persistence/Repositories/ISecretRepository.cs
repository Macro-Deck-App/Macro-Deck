using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface ISecretRepository
{
	Task<SecretEntity?> GetById(Guid id);

	Task Create(SecretEntity secret);

	Task Update(SecretEntity secret);

	Task TryDeleteById(Guid id);
}
