using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IRefreshTokenRepository
{
	Task<RefreshTokenEntity?> GetByTokenHash(string tokenHash);

	Task<RefreshTokenEntity?> GetById(Guid id);

	Task Create(RefreshTokenEntity token);

	Task Update(RefreshTokenEntity token);

	Task<bool> TryRevoke(Guid id, DateTime revokedAt, Guid? replacedById = null, bool rotatedByGrace = false);

	Task RevokeAllForUser(Guid userId, DateTime revokedAt);

	Task<int> RevokeFamily(Guid familyId, DateTime revokedAt);

	Task RevokeAllForDevice(Guid deviceId, DateTime revokedAt);

	Task<IReadOnlyList<Guid>> GetDeviceIdsWithLiveTokens(DateTime now);

	Task DeleteExpired(DateTime now);
}
