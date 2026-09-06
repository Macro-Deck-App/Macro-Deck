using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class IntegrationConfigEntryRepository : IIntegrationConfigEntryRepository
{
	private readonly DatabaseContext _context;

	public IntegrationConfigEntryRepository(DatabaseContext context)
	{
		_context = context;
	}

	public async Task<IReadOnlyList<IntegrationConfigEntryEntity>> GetByIntegrationId(string integrationId)
		=> await _context.IntegrationConfigEntries
			.Where(e => e.IntegrationId == integrationId)
			.OrderBy(e => e.CreatedAt)
			.ToListAsync();

	public Task<IntegrationConfigEntryEntity?> GetById(Guid id)
		=> _context.IntegrationConfigEntries.FirstOrDefaultAsync(e => e.Id == id);

	public async Task Create(IntegrationConfigEntryEntity entry)
	{
		await _context.IntegrationConfigEntries.AddAsync(entry);
		await _context.SaveChangesAsync();
	}

	public async Task Update(IntegrationConfigEntryEntity entry)
	{
		_context.IntegrationConfigEntries.Update(entry);
		await _context.SaveChangesAsync();
	}

	public Task TryDeleteById(Guid id)
		=> _context.IntegrationConfigEntries.Where(e => e.Id == id).ExecuteDeleteAsync();
}
