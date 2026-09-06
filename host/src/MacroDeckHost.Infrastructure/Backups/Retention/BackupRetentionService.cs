using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Retention;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Backups.Retention;

public sealed class BackupRetentionService : IBackupRetentionService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IBackupCatalog _catalog;
	private readonly IBackupStorageRegistry _storageRegistry;
	private readonly IReadOnlyList<IBackupRetentionPolicy> _policies;
	private readonly ILogger _logger;

	public BackupRetentionService(IServiceScopeFactory scopeFactory,
		IBackupCatalog catalog,
		IBackupStorageRegistry storageRegistry,
		IEnumerable<IBackupRetentionPolicy> policies,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_catalog = catalog;
		_storageRegistry = storageRegistry;
		_policies = [.. policies];
		_logger = logger.ForContext<BackupRetentionService>();
	}

	public async Task Apply(CancellationToken cancellationToken = default)
	{
		var settings = await ReadSettings();
		var policy = ResolvePolicy(settings.RetentionPolicy);
		if (policy is null)
		{
			return;
		}

		var backups = await _catalog.List(cancellationToken);
		var retentionSettings = new BackupRetentionSettings(policy.PolicyId, settings.RetentionKeepLatest);

		foreach (var providerBackups in backups.GroupBy(backup => backup.ProviderId))
		{
			var selected = policy.SelectForDeletion([.. providerBackups], retentionSettings);

			foreach (var backup in selected)
			{
				if (BackupRetentionRules.IsExempt(backup))
				{
					continue;
				}

				await DeleteBackup(backup, cancellationToken);
			}
		}
	}

	private IBackupRetentionPolicy? ResolvePolicy(string policyId)
		=> _policies.FirstOrDefault(policy => string.Equals(policy.PolicyId, policyId, StringComparison.Ordinal)) ??
			_policies.FirstOrDefault(policy =>
				string.Equals(policy.PolicyId, KeepLatestBackupRetentionPolicy.Id, StringComparison.Ordinal));

	private async Task DeleteBackup(BackupDescriptor backup, CancellationToken cancellationToken)
	{
		var provider = _storageRegistry.Find(backup.ProviderId);
		if (provider is null)
		{
			return;
		}

		try
		{
			var result = await provider.Delete(backup.StorageId, cancellationToken);
			if (result.Success)
			{
				_logger.Information("Retention removed backup {BackupId} ({Name}) from provider {ProviderId}",
					backup.BackupId,
					backup.Name,
					backup.ProviderId);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}

	private async Task<BackupSettings> ReadSettings()
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		return await preferences.GetBackups();
	}
}
