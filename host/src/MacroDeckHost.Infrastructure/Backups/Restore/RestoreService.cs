using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EvolveDb;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using System.Collections.Concurrent;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;
using MacroDeckHost.Infrastructure.Persistence;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Backups.Restore;

public sealed class RestoreService : IRestoreService
{
	private static readonly TimeSpan _leaseTimeout = TimeSpan.FromSeconds(60);
	private const int _supportedEncryptionVersion = 1;

	private readonly IBackupOperationGate _gate;
	private readonly IBackupProgressReporter _progress;
	private readonly IBackupCatalog _catalog;
	private readonly IBackupStorageRegistry _storageRegistry;
	private readonly IBackupArchiveReader _archiveReader;
	private readonly IBackupService _backupService;
	private readonly IApplicationRestartService _restart;
	private readonly IMacroDeckPaths _paths;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly DurableJsonFile _pendingStore;
	private readonly IKeyRingProtectionService _keyRing;
	private readonly ILogger _logger;
	private readonly Lock _pendingSync = new();

	// Held between Prepare and Commit only, and never written to the staging directory: a plaintext key
	// beside the ring it protects would defeat the point of wrapping the ring at all.
	private readonly ConcurrentDictionary<Guid, byte[]> _stagedKeks = new();

	private PendingRestoreInfo? _cachedPending;
	private bool _pendingLoaded;

	public RestoreService(IBackupOperationGate gate,
		IBackupProgressReporter progress,
		IBackupCatalog catalog,
		IBackupStorageRegistry storageRegistry,
		IBackupArchiveReader archiveReader,
		IBackupService backupService,
		IApplicationRestartService restart,
		IMacroDeckPaths paths,
		IServiceScopeFactory scopeFactory,
		TimeProvider time,
		IKeyRingProtectionService keyRing,
		ILogger logger)
	{
		_keyRing = keyRing;
		_logger = logger.ForContext<RestoreService>();
		_gate = gate;
		_progress = progress;
		_catalog = catalog;
		_storageRegistry = storageRegistry;
		_archiveReader = archiveReader;
		_backupService = backupService;
		_restart = restart;
		_paths = paths;
		_scopeFactory = scopeFactory;
		_time = time;
		_pendingStore = new DurableJsonFile("pending restore",
			PersistenceJsonOptions.Default,
			logger.ForContext<RestoreService>());
	}

	public PendingRestoreInfo? Pending
	{
		get
		{
			lock (_pendingSync)
			{
				if (_pendingLoaded)
				{
					return _cachedPending;
				}

				_cachedPending = ReadPendingFromDisk();
				_pendingLoaded = true;

				return _cachedPending;
			}
		}
	}

	public async Task<Result<PreparedRestore, BackupError>> Prepare(PrepareRestoreRequest request,
		bool proceedWithoutSafetyBackup = false,
		CancellationToken cancellationToken = default)
	{
		var leaseResult = await _gate.Acquire(BackupOperationKind.Restore,
			BackupTrigger.BeforeRestore,
			_leaseTimeout,
			cancellationToken);
		if (!leaseResult.Success)
		{
			return Result.Fail<PreparedRestore, BackupError>(leaseResult.Error!.Value, leaseResult.ErrorMessage);
		}

		var lease = leaseResult.Data!;
		var leaseHeld = true;

		void ReleaseLease()
		{
			if (leaseHeld)
			{
				lease.Dispose();
				leaseHeld = false;
			}
		}

		var operationId = lease.OperationId;
		var restoreId = Guid.NewGuid();
		string? stagingDirectory = null;
		var succeeded = false;

		try
		{
			ReportStatus(operationId, BackupOperationStage.Preparing, request.BackupId);

			var descriptor = await _catalog.Find(request.BackupId, cancellationToken);
			if (descriptor is null)
			{
				return Fail(operationId, request.BackupId, BackupError.NotFound, "The backup was not found.");
			}

			var provider = _storageRegistry.Find(descriptor.ProviderId);
			if (provider is null)
			{
				return Fail(operationId,
					request.BackupId,
					BackupError.ProviderUnavailable,
					"The storage provider is not available.");
			}

			var opened = await provider.OpenRead(descriptor.StorageId, cancellationToken);
			if (!opened.Success)
			{
				return Fail(operationId, request.BackupId, opened.Error!.Value, opened.ErrorMessage);
			}

			await using var archive = opened.Data!;

			var manifest = _archiveReader.ReadManifest(archive);
			if (manifest is null)
			{
				return Fail(operationId,
					request.BackupId,
					BackupError.InvalidArchive,
					"The archive manifest could not be read.");
			}

			if (manifest.FormatVersion > BackupArchiveManifest.CurrentFormatVersion)
			{
				return Fail(operationId,
					request.BackupId,
					BackupError.UnsupportedVersion,
					"This archive was created by a newer version of Macro Deck.");
			}

			if (manifest.Encryption.Version != _supportedEncryptionVersion)
			{
				return Fail(operationId,
					request.BackupId,
					BackupError.UnsupportedEncryption,
					"This archive uses an encryption scheme this version of Macro Deck does not support.");
			}

			var keyResult = await ResolveRecoveryKey(manifest, request.RecoveryKey, cancellationToken);
			if (!keyResult.Success)
			{
				return Fail(operationId, request.BackupId, keyResult.Error!.Value, keyResult.ErrorMessage);
			}

			stagingDirectory = Path.Combine(_paths.RestoreStagingDirectory, restoreId.ToString("N"));
			var applyDirectory = Path.Combine(stagingDirectory, "apply");
			Directory.CreateDirectory(applyDirectory);

			ReportStatus(operationId, BackupOperationStage.Validating, request.BackupId);

			var payloadPath = Path.Combine(stagingDirectory, "payload.zip");
			await _archiveReader.DecryptPayload(archive,
				keyResult.Data!,
				payloadPath,
				cancellationToken: cancellationToken);

			var selection = BackupComponentSelection.Expand(request.Components,
				BackupComponentGroups.AllIds.Except(request.Components).ToList());
			var effective = selection.Effective.ToHashSet();

			List<PendingRestoreFile> files;
			List<string> tables;
			string? databasePath;

			using (var payloadZip = ZipFile.OpenRead(payloadPath))
			{
				var indexEntry = payloadZip.GetEntry(BackupFileNames.ContentIndexEntry);
				if (indexEntry is null)
				{
					return Fail(operationId,
						request.BackupId,
						BackupError.InvalidArchive,
						"The archive content index is missing.");
				}

				BackupContentIndex? contentIndex;
				await using (var indexStream = indexEntry.Open())
				{
					contentIndex = await JsonSerializer.DeserializeAsync<BackupContentIndex>(indexStream,
						PersistenceJsonOptions.Default,
						cancellationToken);
				}

				if (contentIndex is null)
				{
					return Fail(operationId,
						request.BackupId,
						BackupError.InvalidArchive,
						"The archive content index could not be read.");
				}

				var filesResult = await ExtractFiles(payloadZip,
					contentIndex.Entries.Where(entry => effective.Contains(entry.Component)),
					applyDirectory,
					cancellationToken);
				if (!filesResult.Success)
				{
					return Fail(operationId, request.BackupId, filesResult.Error!.Value, filesResult.ErrorMessage);
				}

				files = filesResult.Data!;
				StashArchiveKek(restoreId, applyDirectory, keyResult.Data!);
				tables =
				[
					.. selection.Effective
						.SelectMany(group => BackupComponentGroups.Definition(group).Tables)
						.Distinct()
				];

				if (tables.Count > 0)
				{
					var databaseResult = await ExtractDatabase(payloadZip,
						contentIndex,
						stagingDirectory,
						cancellationToken);
					if (!databaseResult.Success)
					{
						return Fail(operationId,
							request.BackupId,
							databaseResult.Error!.Value,
							databaseResult.ErrorMessage);
					}

					databasePath = databaseResult.Data;
				}
				else
				{
					databasePath = null;
				}
			}

			if (databasePath is not null)
			{
				// The applier copies rows column-by-column into the current schema, so an archive from an
				// older Macro Deck has to be migrated to the current schema on this staged copy - before
				// anything is applied - or the copy at boot would fail after files were already swapped.
				try
				{
					DatabaseMigrationHelper.Migrate(databasePath, _paths.DatabaseMigrationsDirectory);
				}
				catch (EvolveException)
				{
					return Fail(operationId,
						request.BackupId,
						BackupError.UnsupportedVersion,
						"This backup is from a version of Macro Deck that is incompatible with this installation.");
				}
			}

			ReportStatus(operationId, BackupOperationStage.Restoring, request.BackupId);

			// Marked pending before the lease is released, so nothing else can slip into the gate while the
			// safety backup runs. The gate admits BeforeRestore specifically for this, which is also why the
			// lease has to be released first: the safety backup goes through IBackupService.Create and would
			// otherwise wait on the lease this method still holds.
			_gate.RestorePending = true;
			ReleaseLease();

			Guid? safetyBackupId = null;
			var safetyBackupCreated = false;

			var safetyBackup = await _backupService.Create(new CreateBackupRequest(BackupTrigger.BeforeRestore,
					$"Before restoring backup {request.BackupId:N}",
					Protected: true),
				cancellationToken);

			if (safetyBackup.Success)
			{
				safetyBackupId = safetyBackup.Data!.BackupId;
				safetyBackupCreated = true;
			}
			else if (!proceedWithoutSafetyBackup)
			{
				return Fail(operationId, request.BackupId, safetyBackup.Error!.Value, safetyBackup.ErrorMessage);
			}

			var stagedAt = _time.GetUtcNow();
			var components = selection.Effective.ToList();
			var document = new PendingRestoreDocument
			{
				RestoreId = restoreId,
				BackupId = request.BackupId,
				StagedAt = stagedAt,
				Components = components,
				StagingDirectory = stagingDirectory,
				ApplyDirectory = applyDirectory,
				DatabasePath = databasePath,
				Files = files,
				Tables = tables
			};

			_pendingStore.Write(MarkerPath(), document);

			var info = new PendingRestoreInfo(restoreId, request.BackupId, stagedAt, components);
			SetCachedPending(info);

			await PublishPendingChanged(info, cancellationToken);

			ReportStatus(operationId, BackupOperationStage.Completed, request.BackupId);
			succeeded = true;

			return Result.Ok<PreparedRestore, BackupError>(new PreparedRestore(restoreId,
				request.BackupId,
				selection.Effective,
				selection.AutoSelected,
				selection.Warnings,
				safetyBackupId,
				safetyBackupCreated));
		}
		catch (BackupCryptoException e)
		{
			return Fail(operationId, request.BackupId, MapCryptoError(e.Result), e.Message);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			return Fail(operationId, request.BackupId, BackupError.RestoreStagingFailed, e.Message);
		}
		finally
		{
			ReleaseLease();

			if (!succeeded)
			{
				// Prepare marks the restore pending before the safety backup runs, so any failure after that
				// point has to clear it again or the host refuses every backup until it is restarted.
				_gate.RestorePending = false;

				if (stagingDirectory is not null)
				{
					TryDeleteDirectory(stagingDirectory);
				}

				// A restore that never reached Commit leaves no key behind in memory.
				if (!_gate.RestorePending && _stagedKeks.TryRemove(restoreId, out var abandoned))
				{
					CryptographicOperations.ZeroMemory(abandoned);
				}
			}
		}
	}

	/// <summary>
	/// Recovers the archive's key encryption key from the escrow that travelled with it, while the
	/// recovery key the user supplied for the archive is still in hand. Held in memory only: writing it
	/// into the staging directory would put a plaintext key beside the ring it protects. Restoring onto
	/// a machine whose keystore knows nothing about this installation would otherwise come up locked and
	/// ask for the very key that was just typed.
	/// </summary>
	private void StashArchiveKek(Guid restoreId, string applyDirectory, byte[] recoveryKey)
	{
		var stagedKeys = Path.Combine(applyDirectory, "keys");
		if (!KekEscrowStore.Exists(stagedKeys))
		{
			return;
		}

		var (result, kek) = new KekEscrowStore(_logger, _time).TryOpen(stagedKeys, recoveryKey);
		if (result == KekEscrowResult.Ok && kek is not null)
		{
			_stagedKeks[restoreId] = kek;
		}
	}

	private void ApplyStashedKek(Guid restoreId)
	{
		if (!_stagedKeks.TryRemove(restoreId, out var kek))
		{
			return;
		}

		try
		{
			// A failure here is not fatal: the restore still proceeds and the next boot offers the
			// unlock gate, which is the designed safety net rather than a dead end.
			if (!_keyRing.AdoptKek(kek).Success)
			{
				_logger.Warning("The restored key encryption key could not be stored; the host will start locked");
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
		}
	}

	public Task<Result<CommitRestoreResult, BackupError>> Commit(Guid restoreId,
		CancellationToken cancellationToken = default)
	{
		var pending = Pending;
		if (pending is null || pending.RestoreId != restoreId)
		{
			return Task.FromResult(Result.Fail<CommitRestoreResult, BackupError>(BackupError.NotFound,
				"No matching pending restore was found."));
		}

		ApplyStashedKek(restoreId);

		var requested = _restart.Request("restore");
		var availability = _restart.Availability;

		return Task.FromResult(Result.Ok<CommitRestoreResult, BackupError>(new CommitRestoreResult(requested.Success,
			availability.Supported,
			requested.Success ? null : requested.ErrorMessage ?? availability.Reason)));
	}

	public async Task<Result<BackupError>> Cancel(Guid restoreId, CancellationToken cancellationToken = default)
	{
		var markerPath = MarkerPath();
		var document = _pendingStore.Read<PendingRestoreDocument>(markerPath, repair: false);
		if (document is null || document.RestoreId != restoreId)
		{
			return Result.Fail(BackupError.NotFound, "No matching pending restore was found.");
		}

		_pendingStore.Delete(markerPath);
		TryDeleteDirectory(document.StagingDirectory);

		_gate.RestorePending = false;
		SetCachedPending(null);

		await PublishPendingChanged(null, cancellationToken);

		return Result.Ok<BackupError>();
	}

	private static async Task<Result<List<PendingRestoreFile>, BackupError>> ExtractFiles(ZipArchive payloadZip,
		IEnumerable<BackupContentEntry> entries,
		string applyDirectory,
		CancellationToken cancellationToken)
	{
		var files = new List<PendingRestoreFile>();

		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var zipEntry = payloadZip.GetEntry(BackupFileNames.FilesPrefix + entry.Path);
			if (zipEntry is null)
			{
				return Result.Fail<List<PendingRestoreFile>, BackupError>(BackupError.IntegrityFailure,
					$"The staged file '{entry.Path}' is missing from the archive.");
			}

			var targetPath = Path.Combine(applyDirectory, ToNativePath(entry.Path));
			Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

			await using (var source = zipEntry.Open())
			await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await source.CopyToAsync(target, cancellationToken);
			}

			if (!string.Equals(Sha256(targetPath), entry.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return Result.Fail<List<PendingRestoreFile>, BackupError>(BackupError.IntegrityFailure,
					$"The staged file '{entry.Path}' does not match its recorded checksum.");
			}

			files.Add(new PendingRestoreFile
			{
				RelativePath = entry.Path,
				Sha256 = entry.Sha256,
				Component = entry.Component
			});
		}

		return Result.Ok<List<PendingRestoreFile>, BackupError>(files);
	}

	private static async Task<Result<string, BackupError>> ExtractDatabase(ZipArchive payloadZip,
		BackupContentIndex contentIndex,
		string stagingDirectory,
		CancellationToken cancellationToken)
	{
		var databaseEntry = payloadZip.GetEntry(BackupFileNames.DatabaseEntry);
		if (databaseEntry is null)
		{
			return Result.Fail<string, BackupError>(BackupError.IntegrityFailure,
				"The archive is missing its database.");
		}

		var databasePath = Path.Combine(stagingDirectory, "database.db");

		await using (var source = databaseEntry.Open())
		await using (var target = new FileStream(databasePath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			await source.CopyToAsync(target, cancellationToken);
		}

		var expectedHash = contentIndex.Database?.Sha256;
		if (!string.IsNullOrEmpty(expectedHash) &&
			!string.Equals(Sha256(databasePath), expectedHash, StringComparison.OrdinalIgnoreCase))
		{
			return Result.Fail<string, BackupError>(BackupError.IntegrityFailure,
				"The staged database does not match its recorded checksum.");
		}

		return Result.Ok<string, BackupError>(databasePath);
	}

	private async Task<Result<byte[], BackupError>> ResolveRecoveryKey(BackupArchiveManifest manifest,
		string? providedKey,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var recoveryKeyService = scope.ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>();

		var resolved = await recoveryKeyService.Resolve(cancellationToken);
		var localKey = resolved.Success ? resolved.Data : null;
		var localKeyId = localKey is not null ? BackupKeyDerivation.DeriveKeyId(localKey) : null;

		if (localKeyId is not null && string.Equals(manifest.RecoveryKeyId, localKeyId, StringComparison.Ordinal))
		{
			return Result.Ok<byte[], BackupError>(localKey!);
		}

		if (string.IsNullOrWhiteSpace(providedKey))
		{
			return Result.Fail<byte[], BackupError>(BackupError.RecoveryKeyRequired,
				"A recovery key is required to restore this backup.");
		}

		if (!recoveryKeyService.TryParseExportedKey(providedKey, out var parsedKey))
		{
			return Result.Fail<byte[], BackupError>(BackupError.RecoveryKeyInvalid,
				"The recovery key could not be parsed.");
		}

		return Result.Ok<byte[], BackupError>(parsedKey);
	}

	private async Task PublishPendingChanged(PendingRestoreInfo? pending, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
		await publisher.Publish(new RestorePendingChangedNotification(pending, _restart.Availability.Supported),
			cancellationToken);
	}

	private void SetCachedPending(PendingRestoreInfo? info)
	{
		lock (_pendingSync)
		{
			_cachedPending = info;
			_pendingLoaded = true;
		}
	}

	private PendingRestoreInfo? ReadPendingFromDisk()
	{
		var document = _pendingStore.Read<PendingRestoreDocument>(MarkerPath(), repair: false);

		return document is null
			? null
			: new PendingRestoreInfo(document.RestoreId, document.BackupId, document.StagedAt, document.Components);
	}

	private string MarkerPath() => Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);

	private void ReportStatus(Guid operationId,
		BackupOperationStage stage,
		Guid? backupId,
		BackupError? error = null,
		string? errorMessage = null)
		=> _progress.Report(new BackupOperationStatus(operationId,
			BackupOperationKind.Restore,
			stage,
			BackupTrigger.BeforeRestore,
			null,
			null,
			null,
			backupId,
			error,
			errorMessage,
			_time.GetUtcNow()));

	private Result<PreparedRestore, BackupError> Fail(Guid operationId,
		Guid? backupId,
		BackupError error,
		string? errorMessage)
	{
		ReportStatus(operationId, BackupOperationStage.Failed, backupId, error, errorMessage);
		return Result.Fail<PreparedRestore, BackupError>(error, errorMessage);
	}

	private static BackupError MapCryptoError(BackupDecryptResult result)
		=> result switch
		{
			BackupDecryptResult.WrongKey => BackupError.RecoveryKeyInvalid,
			BackupDecryptResult.Corrupt or BackupDecryptResult.Truncated => BackupError.IntegrityFailure,
			BackupDecryptResult.UnsupportedAlgorithm => BackupError.UnsupportedEncryption,
			BackupDecryptResult.TooLarge => BackupError.TooLarge,
			_ => BackupError.InvalidArchive
		};

	private static string Sha256(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static string ToNativePath(string relativePath) => relativePath.Replace('/', Path.DirectorySeparatorChar);

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}
}
