using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class PluginAccessTokenRepository : IPluginAccessTokenRepository
{
	private readonly DatabaseContext _context;

	public PluginAccessTokenRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<PluginAccessTokenEntity?> GetById(Guid id)
	{
		return _context.PluginAccessTokens.FirstOrDefaultAsync(t => t.Id == id);
	}

	public Task<PluginAccessTokenEntity?> GetByHash(string tokenHash)
	{
		return _context.PluginAccessTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
	}

	public async Task<IReadOnlyList<PluginAccessTokenEntity>> GetAll()
	{
		return await _context.PluginAccessTokens.OrderByDescending(t => t.CreatedAt).ToListAsync();
	}

	public async Task Create(PluginAccessTokenEntity token)
	{
		await _context.PluginAccessTokens.AddAsync(token);
		await _context.SaveChangesAsync();
	}

	public Task TouchLastUsed(Guid id, DateTime at)
	{
		return _context.PluginAccessTokens
			.Where(t => t.Id == id)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, at));
	}

	public Task Revoke(Guid id, DateTime at)
	{
		return _context.PluginAccessTokens
			.Where(t => t.Id == id && t.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, at));
	}

	public Task Delete(Guid id)
	{
		return _context.PluginAccessTokens
			.Where(t => t.Id == id)
			.ExecuteDeleteAsync();
	}
}
