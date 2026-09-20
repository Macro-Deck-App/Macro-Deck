using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
	private readonly DatabaseContext _context;

	public RefreshTokenRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<RefreshTokenEntity?> GetByTokenHash(string tokenHash)
		=> _context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

	public Task<RefreshTokenEntity?> GetById(Guid id)
		=> _context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);

	public async Task Create(RefreshTokenEntity token)
	{
		await _context.RefreshTokens.AddAsync(token);
		await _context.SaveChangesAsync();
	}

	public async Task Update(RefreshTokenEntity token)
	{
		_context.RefreshTokens.Update(token);
		await _context.SaveChangesAsync();
	}

	public async Task<bool> TryRevoke(Guid id,
		DateTime revokedAt,
		Guid? replacedById = null,
		bool rotatedByGrace = false)
		=> await _context.RefreshTokens
				.Where(t => t.Id == id && t.RevokedAt == null)
				.ExecuteUpdateAsync(s => s
					.SetProperty(t => t.RevokedAt, revokedAt)
					.SetProperty(t => t.ReplacedById, replacedById)
					.SetProperty(t => t.RotatedByGrace, rotatedByGrace)) ==
			1;

	public Task RevokeAllForUser(Guid userId, DateTime revokedAt)
		=> _context.RefreshTokens
			.Where(t => t.UserId == userId && t.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt));

	public Task<int> RevokeFamily(Guid familyId, DateTime revokedAt)
		=> _context.RefreshTokens
			.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt));

	public Task RevokeAllForDevice(Guid deviceId, DateTime revokedAt)
		=> _context.RefreshTokens
			.Where(t => t.DeviceId == deviceId && t.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt));

	public async Task<IReadOnlyList<Guid>> GetDeviceIdsWithLiveTokens(DateTime now)
		=> await _context.RefreshTokens
			.Where(t => t.DeviceId != null && t.RevokedAt == null && t.ExpiresAt >= now)
			.Select(t => t.DeviceId!.Value)
			.Distinct()
			.ToListAsync();

	public Task DeleteExpired(DateTime now)
	{
		var revokedBefore = now - AuthDefaults.RevokedRefreshTokenRetention;
		return _context.RefreshTokens
			.Where(t => t.ExpiresAt < now || (t.RevokedAt != null && t.RevokedAt < revokedBefore))
			.ExecuteDeleteAsync();
	}
}
