using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class PreUpdateBackupCoordinator : IPreUpdateBackupCoordinator
{
	private const string KeyRingLockedReason = "KeyRingLocked";

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _backedUpBatchIds = [];

	public PreUpdateBackupCoordinator(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public bool IsRestorePending
	{
		get
		{
			using var scope = _scopeFactory.CreateScope();

			return scope.ServiceProvider.GetRequiredService<IRestoreService>().Pending is not null;
		}
	}

	public async Task<PreUpdateBackupOutcome> CreateBeforeHostUpdate(
		string? version,
		CancellationToken cancellationToken = default)
	{
		// A backup needs the recovery key, which a locked key ring cannot read and could be made to mint
		// under scratch keys. Skipping lets the update that may fix the lock still be installed.
		if (IsKeyRingLocked())
		{
			return new PreUpdateBackupOutcome(true, true, KeyRingLockedReason, null);
		}

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

	public async Task<PreUpdateBackupOutcome> EnsureBeforePluginUpdate(
		string pluginId,
		string? batchId,
		Action? starting = null,
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
				alreadyBackedUp = _backedUpBatchIds.Contains(batchId);
			}

			if (alreadyBackedUp)
			{
				return new PreUpdateBackupOutcome(true,
					true,
					"A backup for this update batch was already taken.",
					null);
			}
		}

		starting?.Invoke();

		var request = new CreateBackupRequest(BackupTrigger.BeforePluginUpdate, $"Before installing {pluginId}");
		var outcome = await CreateBackup(request, cancellationToken);

		if (batchId is not null && outcome.Success)
		{
			lock (_sync)
			{
				_backedUpBatchIds.Add(batchId);
			}
		}

		return outcome;
	}

	private async Task<PreUpdateBackupOutcome> CreateBackup(
		CreateBackupRequest request,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
		var result = await backupService.Create(request, cancellationToken);

		return result.Success
			? new PreUpdateBackupOutcome(true, false, null, result.Data!.BackupId)
			: new PreUpdateBackupOutcome(false, false, result.ErrorMessage, null)
			{
				Cancelled = result.Error == BackupError.Cancelled
			};
	}

	private bool IsKeyRingLocked()
	{
		using var scope = _scopeFactory.CreateScope();

		return scope.ServiceProvider.GetRequiredService<IKeyRingProtectionService>().Status.State
			== KeyRingProtectionState.Locked;
	}

	private async Task<BackupSettings> GetBackupSettings()
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();

		return await preferences.GetBackups();
	}
}
