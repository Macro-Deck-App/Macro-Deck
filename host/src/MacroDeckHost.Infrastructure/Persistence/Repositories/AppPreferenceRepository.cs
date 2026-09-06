using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class AppPreferenceRepository : IAppPreferenceRepository
{
	private readonly DatabaseContext _context;

	public AppPreferenceRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<AppPreferenceEntity?> GetByKey(string key)
		=> _context.AppPreferences.FirstOrDefaultAsync(e => e.Key == key);

	public async Task SetValue(string key, string value)
	{
		var now = DateTime.Now;
		var existing = await _context.AppPreferences.FirstOrDefaultAsync(e => e.Key == key);

		if (existing is null)
		{
			await _context.AppPreferences.AddAsync(new AppPreferenceEntity
			{
				Key = key,
				Value = value,
				CreatedAt = now,
				UpdatedAt = now
			});
		}
		else
		{
			existing.Value = value;
			existing.UpdatedAt = now;
			_context.AppPreferences.Update(existing);
		}

		await _context.SaveChangesAsync();
	}
}
