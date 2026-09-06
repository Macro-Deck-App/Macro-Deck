using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class PreUpdateBackupCoordinator : IPreUpdateBackupCoordinator
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _backedUpBatchIds = [];

	public PreUpdateBackupCoordinator(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

	public bool IsRestorePending
	{
		get
		{
			using var scope = _scopeFactory.CreateScope();

			return scope.ServiceProvider.GetRequiredService<IRestoreService>().Pending is not null;
		}
	}

	public async Task<PreUpdateBackupOutcome> CreateBeforeHostUpdate(string? version,
		CancellationToken cancellationToken = default)
	{
		var settings = await GetBackupSettings();
		if (!settings.BeforeHostUpdate)
		{
			return new PreUpdateBackupOutcome(true, true, null, null);
		}

		if (IsRestorePending)
		{
			return new PreUpdateBackupOutcome(false,
				false,
				"A restore is pending; refusing to create a backup before updating.",
				null);
		}

		var request = new CreateBackupRequest(BackupTrigger.BeforeHostUpdate, $"Before updating to {version}");

		return await CreateBackup(request, cancellationToken);
	}

	public async Task<PreUpdateBackupOutcome> EnsureBeforePluginUpdate(string pluginId,
		string? batchId,
		CancellationToken cancellationToken = default)
	{
		var settings = await GetBackupSettings();
		if (!settings.BeforePluginUpdate)
		{
			return new PreUpdateBackupOutcome(true, true, null, null);
		}

		if (batchId is not null)
		{
			bool alreadyBackedUp;
			lock (_sync)
			{
				alreadyBackedUp = !_backedUpBatchIds.Add(batchId);
			}

			if (alreadyBackedUp)
			{
				return new PreUpdateBackupOutcome(true,
					true,
					"A backup for this update batch was already taken.",
					null);
			}
		}

		var request = new CreateBackupRequest(BackupTrigger.BeforePluginUpdate, $"Before installing {pluginId}");

		return await CreateBackup(request, cancellationToken);
	}

	private async Task<PreUpdateBackupOutcome> CreateBackup(CreateBackupRequest request,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
		var result = await backupService.Create(request, cancellationToken);

		return result.Success
			? new PreUpdateBackupOutcome(true, false, null, result.Data!.BackupId)
			: new PreUpdateBackupOutcome(false, false, result.ErrorMessage, null);
	}

	private async Task<BackupSettings> GetBackupSettings()
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();

		return await preferences.GetBackups();
	}
}
