using System.Collections.Concurrent;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Backups;

internal static class LocalRecoveryKey
{
	public static async Task<string?> ResolveId(IServiceScopeFactory scopeFactory, CancellationToken cancellationToken)
	{
		var (keyId, _) = await Resolve(scopeFactory, cancellationToken);
		return keyId;
	}

	public static async Task<(string? KeyId, byte[]? Key)> Resolve(IServiceScopeFactory scopeFactory,
		CancellationToken cancellationToken)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		var recoveryKeyService = scope.ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>();
		var resolved = await recoveryKeyService.Resolve(cancellationToken);

		return resolved.Success && resolved.Data is { } key
			? (BackupKeyDerivation.DeriveKeyId(key), key)
			: (null, null);
	}
}

internal sealed class ImportedBackupsDocument
{
	public List<Guid> BackupIds { get; set; } = [];
}

internal static class ImportedBackupRegistry
{
	private const string FileName = "imported-backups.json";

	public static string ResolvePath(IMacroDeckPaths paths) => Path.Combine(paths.BackupsDirectory, FileName);
}

public sealed class BackupCatalog : IBackupCatalog
{
	private readonly IBackupStorageRegistry _registry;
	private readonly IBackupArchiveReader _archiveReader;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IMacroDeckPaths _paths;
	private readonly DurableJsonFile _importedStore;
	private readonly ConcurrentDictionary<CacheKey, BackupDescriptor> _cache = new();

	public BackupCatalog(IBackupStorageRegistry registry,
		IBackupArchiveReader archiveReader,
		IServiceScopeFactory scopeFactory,
		IMacroDeckPaths paths,
		ILogger logger)
	{
		_registry = registry;
		_archiveReader = archiveReader;
		_scopeFactory = scopeFactory;
		_paths = paths;
		_importedStore = new DurableJsonFile("imported backups",
			PersistenceJsonOptions.Default,
			logger.ForContext<BackupCatalog>());
	}

	public async Task<IReadOnlyList<BackupDescriptor>> List(CancellationToken cancellationToken = default)
	{
		var localKeyId = await LocalRecoveryKey.ResolveId(_scopeFactory, cancellationToken);
		var importedIds = ReadImportedBackupIds();
		var live = new HashSet<CacheKey>();
		var result = new List<BackupDescriptor>();

		foreach (var provider in _registry.Providers)
		{
			try
			{
				await foreach (var obj in provider.List(cancellationToken))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var key = new CacheKey(obj.ProviderId, obj.StorageId, obj.SizeBytes);
					live.Add(key);

					if (!_cache.TryGetValue(key, out var descriptor))
					{
						var built = await BuildDescriptor(provider, obj, localKeyId, importedIds, cancellationToken);
						if (built is null)
						{
							continue;
						}

						descriptor = built;
						_cache[key] = descriptor;
					}

					result.Add(descriptor);
				}
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
			}
		}

		foreach (var key in _cache.Keys)
		{
			if (!live.Contains(key))
			{
				_cache.TryRemove(key, out _);
			}
		}

		return result;
	}

	public async Task<BackupDescriptor?> Find(Guid backupId, CancellationToken cancellationToken = default)
	{
		var backups = await List(cancellationToken);
		return backups.FirstOrDefault(backup => backup.BackupId == backupId);
	}

	public Task Invalidate()
	{
		_cache.Clear();
		return Task.CompletedTask;
	}

	private async Task<BackupDescriptor?> BuildDescriptor(IBackupStorageProvider provider,
		BackupStorageObject obj,
		string? localKeyId,
		HashSet<Guid> importedIds,
		CancellationToken cancellationToken)
	{
		try
		{
			var opened = await provider.OpenRead(obj.StorageId, cancellationToken);
			if (!opened.Success || opened.Data is not { } stream)
			{
				return null;
			}

			BackupArchiveManifest? manifest;
			await using (stream)
			{
				manifest = _archiveReader.ReadManifest(stream);
			}

			if (manifest is null)
			{
				return null;
			}

			var decryptableLocally = localKeyId is not null &&
				string.Equals(manifest.RecoveryKeyId, localKeyId, StringComparison.Ordinal);

			return new BackupDescriptor(manifest.BackupId,
				obj.ProviderId,
				obj.StorageId,
				obj.Name,
				manifest.CreatedAt,
				manifest.Trigger,
				manifest.MacroDeckVersion,
				manifest.FormatVersion,
				manifest.Encryption.Version,
				obj.SizeBytes,
				provider.Capabilities.IsRemote,
				decryptableLocally,
				importedIds.Contains(manifest.BackupId),
				manifest.Note,
				[.. manifest.Components]);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			return null;
		}
	}

	private HashSet<Guid> ReadImportedBackupIds()
	{
		var path = ImportedBackupRegistry.ResolvePath(_paths);
		var document = _importedStore.Read<ImportedBackupsDocument>(path, repair: false);
		return document is null ? [] : [.. document.BackupIds];
	}

	private readonly record struct CacheKey(string ProviderId, string StorageId, long SizeBytes);
}
