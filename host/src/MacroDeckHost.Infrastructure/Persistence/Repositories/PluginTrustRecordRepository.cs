using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class PluginTrustRecordRepository : IPluginTrustRecordRepository
{
	private readonly DatabaseContext _context;

	public PluginTrustRecordRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<PluginTrustRecordEntity?> GetVersion(string pluginId, string version)
		=> _context.PluginTrustRecords
			.FirstOrDefaultAsync(r => r.PluginId == pluginId && r.Version == version);

	public async Task<IReadOnlyList<PluginTrustRecordEntity>> GetForPlugin(string pluginId)
		=> await _context.PluginTrustRecords.Where(r => r.PluginId == pluginId).ToListAsync();

	public async Task Upsert(string pluginId,
		string version,
		string admittedVerdict,
		string? certificateId,
		DateTime installedAt)
	{
		var existing = await _context.PluginTrustRecords
			.FirstOrDefaultAsync(r => r.PluginId == pluginId && r.Version == version);

		if (existing is null)
		{
			var inserted = new PluginTrustRecordEntity
			{
				Id = Guid.CreateVersion7(),
				PluginId = pluginId,
				Version = version,
				AdmittedVerdict = admittedVerdict,
				CertificateId = certificateId,
				InstalledAt = installedAt,
				CreatedAt = installedAt
			};

			_context.PluginTrustRecords.Add(inserted);

			try
			{
				await _context.SaveChangesAsync();
				return;
			}
			catch (DbUpdateException)
			{
				// (PluginId, Version) is unique-indexed and this row is written from three independent
				// places (the baseline backfill, the installer's admit, the supervisor's launch-time
				// backfill/upgrade) with no shared lock - a concurrent insert for the same key can win the
				// race between our own read and write. Detach the failed insert and fall through to update
				// the row the other writer landed, rather than surface a 500 that leaves a running plugin
				// with no trust record.
				_context.Entry(inserted).State = EntityState.Detached;
				existing = await _context.PluginTrustRecords
					.FirstOrDefaultAsync(r => r.PluginId == pluginId && r.Version == version);

				if (existing is null)
				{
					throw;
				}
			}
		}

		existing.AdmittedVerdict = admittedVerdict;
		existing.CertificateId = certificateId;
		existing.InstalledAt = installedAt;

		await _context.SaveChangesAsync();
	}

	public Task DeleteForPlugin(string pluginId)
		=> _context.PluginTrustRecords.Where(r => r.PluginId == pluginId).ExecuteDeleteAsync();

	public Task DeleteVersion(string pluginId, string version)
		=> _context.PluginTrustRecords
			.Where(r => r.PluginId == pluginId && r.Version == version)
			.ExecuteDeleteAsync();
}
