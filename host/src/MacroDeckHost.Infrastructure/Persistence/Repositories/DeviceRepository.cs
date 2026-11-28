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
	{
		return _context.Devices.FirstOrDefaultAsync(d => d.Id == id);
	}

	public async Task<IReadOnlyList<DeviceEntity>> GetAll()
	{
		return await _context.Devices.OrderByDescending(d => d.LastSeenAt).ToListAsync();
	}

	public async Task<IReadOnlyList<(Guid Id, DateTime? SessionsRevokedAt)>> GetSessionRevocations()
	{
		var rows = await _context.Devices.Select(d => new { d.Id, d.SessionsRevokedAt }).ToListAsync();
		return [.. rows.Select(row => (row.Id, row.SessionsRevokedAt))];
	}

	public async Task<IReadOnlyList<DeviceEntity>> GetByStartupProfileId(string profileId)
	{
		return await _context.Devices.Where(d => d.StartupProfileId == profileId).ToListAsync();
	}

	public Task<DeviceEntity?> GetByProviderIdentity(string providerId, string providerDeviceId)
	{
		return _context.Devices.FirstOrDefaultAsync(d =>
			d.ProviderId == providerId && d.ProviderDeviceId == providerDeviceId);
	}

	public async Task<IReadOnlyList<DeviceEntity>> GetByProviderId(string providerId)
	{
		return await _context.Devices.Where(d => d.ProviderId == providerId).ToListAsync();
	}

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
	{
		return _context.Devices.Where(d => d.Id == id).ExecuteDeleteAsync();
	}

	public async Task<bool> SetSettingsButtonHidden(Guid id, bool hidden)
	{
		var updated = await _context.Devices
			.Where(d => d.Id == id)
			.ExecuteUpdateAsync(s => s.SetProperty(d => d.SettingsButtonHidden, hidden));

		await ReloadTracked(id);

		return updated > 0;
	}

	public async Task<bool> ClaimStarterProfile(Guid id)
	{
		var updated = await _context.Devices
			.Where(d => d.Id == id && d.StarterProfilePending)
			.ExecuteUpdateAsync(s => s.SetProperty(d => d.StarterProfilePending, false));

		await ReloadTracked(id);

		return updated > 0;
	}

	public async Task<bool> AssignStarterProfile(Guid id, string profileId, string? layoutSnapshot)
	{
		var updated = await _context.Devices
			.Where(d => d.Id == id && d.StartupProfileId == null)
			.ExecuteUpdateAsync(s => s.SetProperty(d => d.StartupProfileId, profileId)
				.SetProperty(d => d.LayoutSnapshot, d => d.LayoutSnapshot ?? layoutSnapshot));

		await ReloadTracked(id);

		return updated > 0;
	}

	// ExecuteUpdate bypasses the change tracker, so a copy this context already tracks is reloaded rather
	// than left stale for a later SaveChanges in the same long-lived scope to write back.
	private async Task ReloadTracked(Guid id)
	{
		var tracked = _context.Devices.Local.FirstOrDefault(d => d.Id == id);
		if (tracked is not null)
		{
			await _context.Entry(tracked).ReloadAsync();
		}
	}

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
	{
		return await _context.Devices.Where(d => d.LastSeenAt < lastSeenBefore).ToListAsync();
	}

	public Task DeleteMany(IReadOnlyCollection<Guid> ids)
	{
		if (ids.Count == 0)
		{
			return Task.CompletedTask;
		}

		return _context.Devices.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync();
	}
}
