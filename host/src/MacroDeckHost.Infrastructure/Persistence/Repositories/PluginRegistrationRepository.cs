using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class PluginRegistrationRepository : IPluginRegistrationRepository
{
	private readonly DatabaseContext _context;

	public PluginRegistrationRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<PluginRegistrationEntity?> GetByPluginId(string pluginId)
		=> _context.PluginRegistrations.FirstOrDefaultAsync(r => r.PluginId == pluginId);

	public async Task<IReadOnlyList<PluginRegistrationEntity>> GetByAccessTokenId(Guid accessTokenId)
		=> await _context.PluginRegistrations.Where(r => r.AccessTokenId == accessTokenId).ToListAsync();

	public async Task<IReadOnlyList<PluginRegistrationEntity>> GetAll()
		=> await _context.PluginRegistrations.OrderByDescending(r => r.CreatedAt).ToListAsync();

	public async Task<IReadOnlyList<PluginRegistrationEntity>> GetNonRevoked()
		=> await _context.PluginRegistrations.Where(r => r.RevokedAt == null).ToListAsync();

	public async Task Create(PluginRegistrationEntity registration)
	{
		await _context.PluginRegistrations.AddAsync(registration);

		try
		{
			await _context.SaveChangesAsync();
		}
		catch (DbUpdateException ex)
		{
			// The unique index on pr_plugin_id is the only constraint this table carries, so any
			// write failure here is that race - two concurrent registrations for the same id.
			throw new PluginRegistrationConflictException(registration.PluginId, ex);
		}
	}

	public async Task<bool> Reactivate(string pluginId,
		string displayName,
		string secretHash,
		Guid? accessTokenId,
		string origin,
		DateTime at)
	{
		var rows = await _context.PluginRegistrations
			.Where(r => r.PluginId == pluginId && r.RevokedAt != null)
			.ExecuteUpdateAsync(s => s
				.SetProperty(r => r.DisplayName, displayName)
				.SetProperty(r => r.SecretHash, secretHash)
				.SetProperty(r => r.AccessTokenId, accessTokenId)
				.SetProperty(r => r.Origin, origin)
				.SetProperty(r => r.RevokedAt, (DateTime?)null)
				.SetProperty(r => r.LastSeenAt, (DateTime?)null)
				.SetProperty(r => r.CreatedAt, at));

		return rows > 0;
	}

	public async Task<bool> RotateSecret(string pluginId,
		string displayName,
		string secretHash,
		Guid? accessTokenId,
		string origin)
	{
		// RevokedAt == null is the concurrency guard, not just a filter: it is what makes this update
		// atomic against a concurrent revoke, so a revoked row can never have its secret silently rotated.
		var rows = await _context.PluginRegistrations
			.Where(r => r.PluginId == pluginId && r.RevokedAt == null)
			.ExecuteUpdateAsync(s => s
				.SetProperty(r => r.DisplayName, displayName)
				.SetProperty(r => r.SecretHash, secretHash)
				.SetProperty(r => r.AccessTokenId, accessTokenId)
				.SetProperty(r => r.Origin, origin)
				.SetProperty(r => r.LastSeenAt, (DateTime?)null));

		return rows > 0;
	}

	public Task TouchLastSeen(string pluginId, DateTime at)
		=> _context.PluginRegistrations
			.Where(r => r.PluginId == pluginId)
			.ExecuteUpdateAsync(s => s.SetProperty(r => r.LastSeenAt, at));

	public Task Revoke(string pluginId, DateTime at)
		=> _context.PluginRegistrations
			.Where(r => r.PluginId == pluginId && r.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, at));

	public async Task<IReadOnlyList<string>> RevokeByAccessTokenId(Guid accessTokenId, DateTime at)
	{
		var affected = await _context.PluginRegistrations
			.Where(r => r.AccessTokenId == accessTokenId && r.RevokedAt == null)
			.Select(r => r.PluginId)
			.ToListAsync();

		if (affected.Count == 0)
		{
			return affected;
		}

		await _context.PluginRegistrations
			.Where(r => r.AccessTokenId == accessTokenId && r.RevokedAt == null)
			.ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, at));

		return affected;
	}

	public async Task<IReadOnlyList<string>> DeleteByAccessTokenId(Guid accessTokenId)
	{
		var affected = await _context.PluginRegistrations
			.Where(r => r.AccessTokenId == accessTokenId)
			.Select(r => r.PluginId)
			.ToListAsync();

		if (affected.Count == 0)
		{
			return affected;
		}

		// pr_access_token_id carries no foreign key, so nothing removes these rows on the token's
		// behalf - without this they outlive the credential they belong to.
		await _context.PluginRegistrations
			.Where(r => r.AccessTokenId == accessTokenId)
			.ExecuteDeleteAsync();

		return affected;
	}
}
