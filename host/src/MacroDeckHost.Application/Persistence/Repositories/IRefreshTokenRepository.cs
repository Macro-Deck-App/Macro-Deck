using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IRefreshTokenRepository
{
	Task<RefreshTokenEntity?> GetByTokenHash(string tokenHash);

	Task Create(RefreshTokenEntity token);

	Task Update(RefreshTokenEntity token);

	Task RevokeAllForUser(Guid userId, DateTime revokedAt);

	Task RevokeAllForDevice(Guid deviceId, DateTime revokedAt);

	Task<IReadOnlyList<Guid>> GetDeviceIdsWithLiveTokens(DateTime now);

	Task DeleteExpired(DateTime now);
}
