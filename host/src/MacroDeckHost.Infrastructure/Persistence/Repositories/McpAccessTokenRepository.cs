using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class McpAccessTokenRepository : IMcpAccessTokenRepository
{
	private readonly DatabaseContext _context;

	public McpAccessTokenRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<McpAccessTokenEntity?> GetById(Guid id)
	{
		return _context.McpAccessTokens.FirstOrDefaultAsync(t => t.Id == id);
	}

	public Task<McpAccessTokenEntity?> GetByHash(string tokenHash)
	{
		return _context.McpAccessTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
	}

	public async Task<IReadOnlyList<McpAccessTokenEntity>> GetAll()
	{
		return await _context.McpAccessTokens.OrderByDescending(t => t.CreatedAt).ToListAsync();
	}

	public async Task Create(McpAccessTokenEntity token)
	{
		await _context.McpAccessTokens.AddAsync(token);
		await _context.SaveChangesAsync();
	}

	public Task TouchLastUsed(Guid id, DateTime at)
	{
		return _context.McpAccessTokens
			.Where(t => t.Id == id)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, at));
	}

	public Task Revoke(Guid id, DateTime at)
	{
		return _context.McpAccessTokens
			.Where(t => t.Id == id && t.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, at));
	}

	public Task Delete(Guid id)
	{
		return _context.McpAccessTokens
			.Where(t => t.Id == id)
			.ExecuteDeleteAsync();
	}
}
