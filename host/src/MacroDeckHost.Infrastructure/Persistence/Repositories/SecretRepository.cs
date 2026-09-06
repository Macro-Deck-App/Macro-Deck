using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class SecretRepository : ISecretRepository
{
	private readonly DatabaseContext _context;

	public SecretRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<SecretEntity?> GetById(Guid id) => _context.Secrets.FirstOrDefaultAsync(s => s.Id == id);

	public async Task Create(SecretEntity secret)
	{
		await _context.Secrets.AddAsync(secret);
		await _context.SaveChangesAsync();
	}

	public async Task Update(SecretEntity secret)
	{
		_context.Secrets.Update(secret);
		await _context.SaveChangesAsync();
	}

	public Task TryDeleteById(Guid id) => _context.Secrets.Where(s => s.Id == id).ExecuteDeleteAsync();
}
