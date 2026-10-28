using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IMcpAccessTokenRepository
{
	Task<McpAccessTokenEntity?> GetById(Guid id);

	Task<McpAccessTokenEntity?> GetByHash(string tokenHash);

	Task<IReadOnlyList<McpAccessTokenEntity>> GetAll();

	Task Create(McpAccessTokenEntity token);

	Task TouchLastUsed(Guid id, DateTime at);

	Task Revoke(Guid id, DateTime at);

	Task Delete(Guid id);
}
