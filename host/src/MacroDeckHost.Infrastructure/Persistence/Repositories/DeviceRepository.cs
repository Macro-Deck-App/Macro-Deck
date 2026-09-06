using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class DeviceRepository : IDeviceRepository
{
	private readonly DatabaseContext _context;

	public DeviceRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<DeviceEntity?> GetById(Guid id)
		=> _context.Devices.FirstOrDefaultAsync(d => d.Id == id);

	public async Task<IReadOnlyList<DeviceEntity>> GetAll()
		=> await _context.Devices.OrderByDescending(d => d.LastSeenAt).ToListAsync();

	public async Task<IReadOnlyList<DeviceEntity>> GetByStartupProfileId(string profileId)
		=> await _context.Devices.Where(d => d.StartupProfileId == profileId).ToListAsync();

	public Task<DeviceEntity?> GetByProviderIdentity(string providerId, string providerDeviceId)
		=> _context.Devices.FirstOrDefaultAsync(d =>
			d.ProviderId == providerId && d.ProviderDeviceId == providerDeviceId);

	public async Task<IReadOnlyList<DeviceEntity>> GetByProviderId(string providerId)
		=> await _context.Devices.Where(d => d.ProviderId == providerId).ToListAsync();

	public async Task Create(DeviceEntity device)
	{
		await _context.Devices.AddAsync(device);
		await _context.SaveChangesAsync();
	}

	public async Task Update(DeviceEntity device)
	{
		_context.Devices.Update(device);
		await _context.SaveChangesAsync();
	}

	public Task Delete(Guid id)
		=> _context.Devices.Where(d => d.Id == id).ExecuteDeleteAsync();

	public Task TouchLastSeen(IReadOnlyCollection<Guid> ids, DateTime seenAt)
	{
		if (ids.Count == 0)
		{
			return Task.CompletedTask;
		}

		return _context.Devices
			.Where(d => ids.Contains(d.Id))
			.ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, seenAt));
	}

	public async Task<IReadOnlyList<DeviceEntity>> GetStale(DateTime lastSeenBefore)
		=> await _context.Devices.Where(d => d.LastSeenAt < lastSeenBefore).ToListAsync();

	public Task DeleteMany(IReadOnlyCollection<Guid> ids)
	{
		if (ids.Count == 0)
		{
			return Task.CompletedTask;
		}

		return _context.Devices.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync();
	}
}
