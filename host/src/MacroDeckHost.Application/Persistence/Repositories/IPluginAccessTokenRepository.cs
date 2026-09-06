using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IPluginAccessTokenRepository
{
	Task<PluginAccessTokenEntity?> GetById(Guid id);

	Task<PluginAccessTokenEntity?> GetByHash(string tokenHash);

	Task<IReadOnlyList<PluginAccessTokenEntity>> GetAll();

	Task Create(PluginAccessTokenEntity token);

	Task TouchLastUsed(Guid id, DateTime at);

	Task Revoke(Guid id, DateTime at);

	Task Delete(Guid id);
}
