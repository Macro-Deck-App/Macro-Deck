using System.IO.Compression;
using System.Security.Cryptography;
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
	private const int SupportedEncryptionVersion = 1;

	private readonly IBackupOperationGate _gate;
	private readonly IBackupOperationCancellation _cancellation;
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

	public RestoreService(
		IBackupOperationGate gate,
		IBackupOperationCancellation cancellation,
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
		_cancellation = cancellation;
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

	public async Task<Result<PreparedRestore, BackupError>> Prepare(
		PrepareRestoreRequest request,
		bool proceedWithoutSafetyBackup = false,
		CancellationToken cancellationToken = default)
	{
		if (_pendingStore.Read<PendingRestoreDocument>(MarkerPath(), false) is { } pending
			&& StagedRestoreApplier.HasBegun(pending))
		{
			return Result.Fail<PreparedRestore, BackupError>(BackupError.RestorePending);
		}

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
		using var cancellation = _cancellation.Begin(operationId, cancellationToken);
		var token = cancellation.Token;
		var restoreId = Guid.NewGuid();
		string? stagingDirectory = null;
		var succeeded = false;

		try
		{
			ReportStatus(operationId, BackupOperationStage.Preparing, request.BackupId);

			var descriptor = await _catalog.Find(request.BackupId, token);
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

			var opened = await provider.OpenRead(descriptor.StorageId, token);
			if (!opened.Success)
			{
				return Fail(operationId, request.BackupId, opened.Error!.Value, opened.ErrorMessage);
			}

			await using var archive = opened.Data!;

			var manifest = _archiveReader.ReadManifest(archive);
			if (manifest is null)
			{
				return Fail(operationId, request.BackupId, BackupError.InvalidArchive, null);
			}

			if (manifest.FormatVersion > BackupArchiveManifest.CurrentFormatVersion)
			{
				return Fail(operationId, request.BackupId, BackupError.UnsupportedVersion, null);
			}

			if (manifest.Encryption.Version != SupportedEncryptionVersion)
			{
				return Fail(operationId, request.BackupId, BackupError.UnsupportedEncryption, null);
			}

			var keyResult = await ResolveRecoveryKey(manifest, request.RecoveryKey, token);
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
				cancellationToken: token);

			var selection = BackupComponentSelection.Expand(request.Components,
				BackupComponentGroups.AllIds.Except(request.Components).ToList());
			var effective = selection.Effective.ToHashSet();

			List<PendingRestoreFile> files;
			List<string> tables;
			string? databasePath;

			using (var payloadZip = ZipFile.OpenRead(payloadPath))
			{
				var indexResult = await BackupContentIndexReader.Read(payloadZip, token);
				if (!indexResult.Success)
				{
					return Refuse(operationId, request.BackupId, indexResult.Error!.Value, indexResult.ErrorMessage);
				}

				var contentIndex = indexResult.Data!;
				var entriesResult = CheckEntries(payloadZip, contentIndex, applyDirectory);
				if (!entriesResult.Success)
				{
					return Refuse(operationId,
						request.BackupId,
						entriesResult.Error!.Value,
						entriesResult.ErrorMessage);
				}

				var filesResult = await ExtractFiles(payloadZip,
					contentIndex.Entries.Where(entry => effective.Contains(entry.Component)),
					applyDirectory,
					token);
				if (!filesResult.Success)
				{
					return filesResult.Error == BackupError.TooLarge
						? Refuse(operationId, request.BackupId, BackupError.TooLarge, filesResult.ErrorMessage)
						: Fail(operationId, request.BackupId, filesResult.Error!.Value, filesResult.ErrorMessage);
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
						token);
					if (!databaseResult.Success)
					{
						return databaseResult.Error == BackupError.TooLarge
							? Refuse(operationId, request.BackupId, BackupError.TooLarge, databaseResult.ErrorMessage)
							: Fail(operationId,
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
					DatabaseMigrationHelper.Migrate(databasePath);
				}
				catch (DatabaseMigrationException ex) when (ex.FromNewerHost)
				{
					return Refuse(operationId, request.BackupId, BackupError.UnsupportedVersion, ex.Message);
				}
				catch (DatabaseMigrationException ex)
				{
					return Refuse(operationId, request.BackupId, BackupError.IncompatibleVersion, ex.Message);
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
					true),
				token);

			if (safetyBackup.Success)
			{
				safetyBackupId = safetyBackup.Data!.BackupId;
				safetyBackupCreated = true;
			}
			else if (!proceedWithoutSafetyBackup || safetyBackup.Error == BackupError.Cancelled)
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
		catch (OperationCanceledException) when (cancellation.IsCancelled || cancellationToken.IsCancellationRequested)
		{
			var cancelled = Fail(operationId, request.BackupId, BackupError.Cancelled, "The restore was cancelled.");
			if (cancellationToken.IsCancellationRequested)
			{
				throw;
			}

			return cancelled;
		}
		catch (BackupCryptoException e)
		{
			return Fail(operationId, request.BackupId, MapCryptoError(e.Result), e.Message);
		}
		catch (InvalidDataException e)
		{
			return Refuse(operationId, request.BackupId, BackupError.InvalidArchive, e.Message);
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

	public Task<Result<CommitRestoreResult, BackupError>> Commit(
		Guid restoreId,
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
		var document = _pendingStore.Read<PendingRestoreDocument>(markerPath, false);
		if (document is null || document.RestoreId != restoreId)
		{
			return Result.Fail(BackupError.NotFound, "No matching pending restore was found.");
		}

		// Its staging directory holds the original files until the next start finishes rolling it back.
		if (StagedRestoreApplier.HasBegun(document))
		{
			return Result.Fail(BackupError.RestorePending);
		}

		_pendingStore.Delete(markerPath);
		TryDeleteDirectory(document.StagingDirectory);

		_gate.RestorePending = false;
		SetCachedPending(null);

		await PublishPendingChanged(null, cancellationToken);

		return Result.Ok<BackupError>();
	}

	private static async Task<Result<List<PendingRestoreFile>, BackupError>> ExtractFiles(
		ZipArchive payloadZip,
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

			var targetPath = BackupArchiveLimits.ResolveInside(applyDirectory, entry.Path);
			if (targetPath is null)
			{
				return Result.Fail<List<PendingRestoreFile>, BackupError>(BackupError.InvalidArchive,
					$"The archive entry '{entry.Path}' points outside the restore folder.");
			}

			Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

			bool withinLimit;
			await using (var source = zipEntry.Open())
			await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				withinLimit = await BackupContentIndexReader.CopyBounded(source,
					target,
					zipEntry.Length,
					cancellationToken);
			}

			if (!withinLimit)
			{
				return Result.Fail<List<PendingRestoreFile>, BackupError>(BackupError.TooLarge,
					$"The archive entry '{entry.Path}' expands beyond its recorded size.");
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

	private static Result<BackupError> CheckEntries(
		ZipArchive payloadZip,
		BackupContentIndex contentIndex,
		string applyDirectory)
	{
		if (payloadZip.Entries.Count > BackupArchiveLimits.MaxInnerEntries
			|| contentIndex.Entries.Count > BackupArchiveLimits.MaxInnerEntries)
		{
			return Result.Fail(BackupError.TooLarge, "The archive holds more entries than a backup can.");
		}

		if (payloadZip.GetEntry(BackupFileNames.DatabaseEntry) is { Length: > BackupArchiveLimits.MaxSingleEntryBytes })
		{
			return Result.Fail(BackupError.TooLarge, "The archive database is too large.");
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var total = 0L;

		foreach (var entry in contentIndex.Entries)
		{
			if (!BackupArchiveLimits.IsRestorableEntry(entry.Path, entry.Component)
				|| BackupArchiveLimits.ResolveInside(applyDirectory, entry.Path) is null
				|| !seen.Add(entry.Path))
			{
				return Result.Fail(BackupError.InvalidArchive,
					$"The archive entry '{entry.Path}' tagged {entry.Component} cannot be restored safely.");
			}

			var size = Math.Max(entry.Size, payloadZip.GetEntry(BackupFileNames.FilesPrefix + entry.Path)?.Length ?? 0);
			total += size;
			if (size > BackupArchiveLimits.MaxSingleEntryBytes || total > BackupArchiveLimits.MaxTotalPlaintextBytes)
			{
				return Result.Fail(BackupError.TooLarge, $"The archive entry '{entry.Path}' is too large to restore.");
			}
		}

		return Result.Ok<BackupError>();
	}

	private static async Task<Result<string, BackupError>> ExtractDatabase(
		ZipArchive payloadZip,
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

		bool withinLimit;
		await using (var source = databaseEntry.Open())
		await using (var target = new FileStream(databasePath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			withinLimit = await BackupContentIndexReader.CopyBounded(source,
				target,
				databaseEntry.Length,
				cancellationToken);
		}

		if (!withinLimit)
		{
			return Result.Fail<string, BackupError>(BackupError.TooLarge,
				"The archive database expands beyond its recorded size.");
		}

		var expectedHash = contentIndex.Database?.Sha256;
		if (!string.IsNullOrEmpty(expectedHash)
			&& !string.Equals(Sha256(databasePath), expectedHash, StringComparison.OrdinalIgnoreCase))
		{
			return Result.Fail<string, BackupError>(BackupError.IntegrityFailure,
				"The staged database does not match its recorded checksum.");
		}

		return Result.Ok<string, BackupError>(databasePath);
	}

	private async Task<Result<byte[], BackupError>> ResolveRecoveryKey(
		BackupArchiveManifest manifest,
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
		var document = _pendingStore.Read<PendingRestoreDocument>(MarkerPath(), false);

		return document is null
			? null
			: new PendingRestoreInfo(document.RestoreId, document.BackupId, document.StagedAt, document.Components);
	}

	private string MarkerPath()
	{
		return Path.Combine(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);
	}

	private void ReportStatus(
		Guid operationId,
		BackupOperationStage stage,
		Guid? backupId,
		BackupError? error = null,
		string? errorMessage = null)
	{
		_progress.Report(new BackupOperationStatus(operationId,
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
	}

	private Result<PreparedRestore, BackupError> Fail(
		Guid operationId,
		Guid? backupId,
		BackupError error,
		string? errorMessage)
	{
		ReportStatus(operationId, BackupOperationStage.Failed, backupId, error, errorMessage);
		return Result.Fail<PreparedRestore, BackupError>(error, errorMessage);
	}

	// The detail quotes archive content an attacker controls, so the user only gets the localized message.
	private Result<PreparedRestore, BackupError> Refuse(
		Guid operationId,
		Guid? backupId,
		BackupError error,
		string? detail)
	{
		_logger.Warning("Refused to restore backup {BackupId}: {Detail}", backupId, detail);

		return Fail(operationId, backupId, error, null);
	}

	private static BackupError MapCryptoError(BackupDecryptResult result)
	{
		return result switch
		{
			BackupDecryptResult.WrongKey => BackupError.RecoveryKeyInvalid,
			BackupDecryptResult.Corrupt or BackupDecryptResult.Truncated => BackupError.IntegrityFailure,
			BackupDecryptResult.UnsupportedAlgorithm => BackupError.UnsupportedEncryption,
			BackupDecryptResult.TooLarge => BackupError.TooLarge,
			_ => BackupError.InvalidArchive
		};
	}

	private static string Sha256(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, true);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}
}
