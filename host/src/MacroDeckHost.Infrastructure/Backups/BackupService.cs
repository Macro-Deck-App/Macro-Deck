using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Retention;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups.Crypto;
using MacroDeckHost.Infrastructure.Persistence;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupService : IBackupService
{
	private static readonly TimeSpan _waitingTriggerTimeout = TimeSpan.FromSeconds(60);

	private readonly IBackupOperationGate _gate;
	private readonly IBackupProgressReporter _progress;
	private readonly IBackupSnapshotSource _snapshotSource;
	private readonly IBackupArchiveWriter _archiveWriter;
	private readonly IBackupArchiveReader _archiveReader;
	private readonly IBackupStorageRegistry _storageRegistry;
	private readonly IBackupCatalog _catalog;
	private readonly IBackupRetentionService _retention;
	private readonly IMacroDeckPaths _paths;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly DurableJsonFile _importedStore;

	public BackupService(IBackupOperationGate gate,
		IBackupProgressReporter progress,
		IBackupSnapshotSource snapshotSource,
		IBackupArchiveWriter archiveWriter,
		IBackupArchiveReader archiveReader,
		IBackupStorageRegistry storageRegistry,
		IBackupCatalog catalog,
		IBackupRetentionService retention,
		IMacroDeckPaths paths,
		IServiceScopeFactory scopeFactory,
		TimeProvider time,
		ILogger logger)
	{
		_gate = gate;
		_progress = progress;
		_snapshotSource = snapshotSource;
		_archiveWriter = archiveWriter;
		_archiveReader = archiveReader;
		_storageRegistry = storageRegistry;
		_catalog = catalog;
		_retention = retention;
		_paths = paths;
		_scopeFactory = scopeFactory;
		_time = time;
		_importedStore = new DurableJsonFile("imported backups",
			PersistenceJsonOptions.Default,
			logger.ForContext<BackupService>());
	}

	public async Task<Result<BackupDescriptor, BackupError>> Create(CreateBackupRequest request,
		CancellationToken cancellationToken = default)
	{
		var leaseResult = await AcquireCreateLease(request.Trigger, cancellationToken);
		if (!leaseResult.Success)
		{
			return Result.Fail<BackupDescriptor, BackupError>(leaseResult.Error!.Value, leaseResult.ErrorMessage);
		}

		using var acquiredLease = leaseResult.Data!;
		var operationId = acquiredLease.OperationId;
		var backupId = Guid.NewGuid();
		var stagingDirectory = Path.Combine(_paths.BackupsDirectory, "staging", operationId.ToString("N"));

		try
		{
			ReportStatus(operationId, BackupOperationStage.Preparing, request.Trigger, backupId);

			byte[] recoveryKey;
			Guid installationId;
			await using (var scope = _scopeFactory.CreateAsyncScope())
			{
				var recoveryKeyService = scope.ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>();
				var ensured = await recoveryKeyService.EnsureCreated(cancellationToken);
				if (!ensured.Success)
				{
					return Fail(operationId, request.Trigger, backupId, ensured.Error!.Value, ensured.ErrorMessage);
				}

				recoveryKey = ensured.Data!;

				var appPreferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
				installationId = await appPreferences.GetInstallationId();
			}

			Directory.CreateDirectory(stagingDirectory);
			var plan = _snapshotSource.Plan();

			var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(stagingDirectory)) ?? stagingDirectory);
			var required = (long)(plan.TotalBytes * 1.2);
			if (drive.AvailableFreeSpace < required)
			{
				return Fail(operationId,
					request.Trigger,
					backupId,
					BackupError.InsufficientDiskSpace,
					"Not enough free space is available to stage the backup.");
			}

			ReportStatus(operationId,
				BackupOperationStage.CreatingSnapshot,
				request.Trigger,
				backupId,
				totalBytes: plan.TotalBytes);

			var databaseCopyPath = Path.Combine(stagingDirectory, "database.db");
			await _snapshotSource.CopyDatabase(databaseCopyPath, cancellationToken);
			var schemaVersion = _snapshotSource.ReadSchemaVersion(databaseCopyPath);

			var manifest = BuildManifest(backupId,
				request,
				installationId,
				recoveryKey,
				schemaVersion,
				_time.GetUtcNow());

			ReportStatus(operationId,
				BackupOperationStage.Encrypting,
				request.Trigger,
				backupId,
				totalBytes: plan.TotalBytes);
			ReportStatus(operationId,
				BackupOperationStage.Saving,
				request.Trigger,
				backupId,
				totalBytes: plan.TotalBytes);

			var archivePath = Path.Combine(stagingDirectory, "archive" + BackupFileNames.Extension);
			await using (var archiveStream
				= new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await _archiveWriter.Write(archiveStream,
					new BackupArchiveWriteRequest(manifest, plan, databaseCopyPath),
					recoveryKey,
					cancellationToken);
			}

			ReportStatus(operationId,
				BackupOperationStage.Validating,
				request.Trigger,
				backupId,
				totalBytes: plan.TotalBytes);

			var validationPath = Path.Combine(stagingDirectory, "validation.tmp");

			// Read the freshly written container back through the same decrypt path a restore or export
			// would use, so a corrupt or truncated archive is caught here instead of being handed to a
			// storage provider that could keep it around indefinitely.
			await using (var validateStream
				= new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				await _archiveReader.DecryptPayload(validateStream,
					recoveryKey,
					validationPath,
					cancellationToken: cancellationToken);
			}

			TryDeleteFile(validationPath);

			var provider = _storageRegistry.Primary;
			var suggestedName = $"{manifest.CreatedAt:yyyyMMddTHHmmssZ}-{backupId:N}";
			var archiveLength = new FileInfo(archivePath).Length;

			var writeResult = await provider.Write(new BackupStorageWriteRequest(suggestedName,
					archiveLength,
					async (destination, ct) =>
					{
						await using var source = File.OpenRead(archivePath);
						await source.CopyToAsync(destination, ct);
					},
					archivePath),
				cancellationToken);

			if (!writeResult.Success)
			{
				return Fail(operationId, request.Trigger, backupId, writeResult.Error!.Value, writeResult.ErrorMessage);
			}

			var descriptor = new BackupDescriptor(manifest.BackupId,
				provider.ProviderId,
				writeResult.Data!.StorageId,
				writeResult.Data.Name,
				manifest.CreatedAt,
				manifest.Trigger,
				manifest.MacroDeckVersion,
				manifest.FormatVersion,
				manifest.Encryption.Version,
				writeResult.Data.SizeBytes,
				provider.Capabilities.IsRemote,
				DecryptableLocally: true,
				Imported: false,
				manifest.Note,
				[.. manifest.Components]);

			ReportStatus(operationId,
				BackupOperationStage.Completed,
				request.Trigger,
				backupId,
				totalBytes: plan.TotalBytes);

			await PublishListChanged("created", cancellationToken);
			await _retention.Apply(cancellationToken);

			return Result.Ok<BackupDescriptor, BackupError>(descriptor);
		}
		catch (BackupCryptoException e)
		{
			return Fail(operationId, request.Trigger, backupId, MapCryptoError(e.Result), e.Message);
		}
		catch (IOException e) when (e is BackupFileLockedException || BackupFileLockedException.IsLockViolation(e))
		{
			return Fail(operationId, request.Trigger, backupId, BackupError.FileLocked, e.Message);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			return Fail(operationId, request.Trigger, backupId, BackupError.SnapshotFailed, e.Message);
		}
		finally
		{
			TryDeleteDirectory(stagingDirectory);
		}
	}

	public async Task<Result<IReadOnlyList<BackupDescriptor>, BackupError>> List(
		CancellationToken cancellationToken = default)
	{
		var backups = await _catalog.List(cancellationToken);
		return Result.Ok<IReadOnlyList<BackupDescriptor>, BackupError>(backups);
	}

	public async Task<Result<BackupInspection, BackupError>> Inspect(BackupSourceRef source,
		string? recoveryKey,
		CancellationToken cancellationToken = default)
	{
		var opened = await OpenSource(source, cancellationToken);
		if (!opened.Success)
		{
			return Result.Fail<BackupInspection, BackupError>(opened.Error!.Value, opened.ErrorMessage);
		}

		var (stream, provider) = opened.Data!;
		await using (stream)
		{
			var manifest = _archiveReader.ReadManifest(stream);
			if (manifest is null)
			{
				return Result.Fail<BackupInspection, BackupError>(BackupError.InvalidArchive,
					"The archive manifest could not be read.");
			}

			if (manifest.FormatVersion > BackupArchiveManifest.CurrentFormatVersion)
			{
				return Result.Fail<BackupInspection, BackupError>(BackupError.UnsupportedVersion,
					"This archive was created by a newer version of Macro Deck.");
			}

			await using var scope = _scopeFactory.CreateAsyncScope();
			var recoveryKeyService = scope.ServiceProvider.GetRequiredService<IBackupRecoveryKeyService>();
			var resolved = await recoveryKeyService.Resolve(cancellationToken);
			var localKey = resolved.Success ? resolved.Data : null;
			var localKeyId = localKey is not null ? BackupKeyDerivation.DeriveKeyId(localKey) : null;
			var recoveryKeyRequired = localKeyId is null ||
				!string.Equals(manifest.RecoveryKeyId, localKeyId, StringComparison.Ordinal);

			byte[]? effectiveKey = null;
			if (!string.IsNullOrWhiteSpace(recoveryKey) &&
				recoveryKeyService.TryParseExportedKey(recoveryKey, out var parsedKey))
			{
				effectiveKey = parsedKey;
			}
			else if (!recoveryKeyRequired)
			{
				effectiveKey = localKey;
			}

			IReadOnlyList<BackupComponentGroupInfo> components;
			try
			{
				components = effectiveKey is null
					? DeclaredComponents(manifest)
					: await ReadComponentsFromPayload(stream, effectiveKey, manifest, cancellationToken);
			}
			catch (BackupCryptoException e)
			{
				return Result.Fail<BackupInspection, BackupError>(MapCryptoError(e.Result), e.Message);
			}

			var name = source.FilePath is not null
				? Path.GetFileName(source.FilePath)
				: source.StorageId ?? string.Empty;
			var descriptor = new BackupDescriptor(manifest.BackupId,
				source.ProviderId ?? string.Empty,
				source.StorageId ?? name,
				name,
				manifest.CreatedAt,
				manifest.Trigger,
				manifest.MacroDeckVersion,
				manifest.FormatVersion,
				manifest.Encryption.Version,
				stream.Length,
				provider?.Capabilities.IsRemote ?? false,
				DecryptableLocally: !recoveryKeyRequired,
				Imported: false,
				manifest.Note,
				[.. manifest.Components]);

			return Result.Ok<BackupInspection, BackupError>(new BackupInspection(descriptor,
				recoveryKeyRequired,
				components));
		}
	}

	public async Task<Result<BackupDescriptor, BackupError>> Import(BackupSourceRef source,
		CancellationToken cancellationToken = default)
	{
		var opened = await OpenSource(source, cancellationToken);
		if (!opened.Success)
		{
			return Result.Fail<BackupDescriptor, BackupError>(opened.Error!.Value, opened.ErrorMessage);
		}

		var (stream, _) = opened.Data!;
		await using (stream)
		{
			var manifest = _archiveReader.ReadManifest(stream);
			if (manifest is null)
			{
				return Result.Fail<BackupDescriptor, BackupError>(BackupError.InvalidArchive,
					"The archive manifest could not be read.");
			}

			if (manifest.FormatVersion > BackupArchiveManifest.CurrentFormatVersion)
			{
				return Result.Fail<BackupDescriptor, BackupError>(BackupError.UnsupportedVersion,
					"This archive was created by a newer version of Macro Deck.");
			}

			var provider = _storageRegistry.Primary;
			var length = stream.Length;
			var suggestedName = $"imported-{manifest.CreatedAt:yyyyMMddTHHmmssZ}-{manifest.BackupId:N}";

			var writeResult = await provider.Write(new BackupStorageWriteRequest(suggestedName,
					length,
					async (destination, ct) =>
					{
						stream.Position = 0;
						await stream.CopyToAsync(destination, ct);
					}),
				cancellationToken);

			if (!writeResult.Success)
			{
				return Result.Fail<BackupDescriptor, BackupError>(writeResult.Error!.Value, writeResult.ErrorMessage);
			}

			MarkImported(manifest.BackupId);

			var localKeyId = await LocalRecoveryKey.ResolveId(_scopeFactory, cancellationToken);
			var descriptor = new BackupDescriptor(manifest.BackupId,
				provider.ProviderId,
				writeResult.Data!.StorageId,
				writeResult.Data.Name,
				manifest.CreatedAt,
				manifest.Trigger,
				manifest.MacroDeckVersion,
				manifest.FormatVersion,
				manifest.Encryption.Version,
				writeResult.Data.SizeBytes,
				provider.Capabilities.IsRemote,
				DecryptableLocally: localKeyId is not null &&
				string.Equals(manifest.RecoveryKeyId, localKeyId, StringComparison.Ordinal),
				Imported: true,
				manifest.Note,
				[.. manifest.Components]);

			await PublishListChanged("imported", cancellationToken);

			return Result.Ok<BackupDescriptor, BackupError>(descriptor);
		}
	}

	public async Task<Result<BackupExportHandle, BackupError>> OpenExport(Guid backupId,
		CancellationToken cancellationToken = default)
	{
		var descriptor = await _catalog.Find(backupId, cancellationToken);
		if (descriptor is null)
		{
			return Result.Fail<BackupExportHandle, BackupError>(BackupError.NotFound, "The backup was not found.");
		}

		var provider = _storageRegistry.Find(descriptor.ProviderId);
		if (provider is null)
		{
			return Result.Fail<BackupExportHandle, BackupError>(BackupError.ProviderUnavailable,
				"The storage provider is not available.");
		}

		var opened = await provider.OpenRead(descriptor.StorageId, cancellationToken);
		if (!opened.Success)
		{
			return Result.Fail<BackupExportHandle, BackupError>(opened.Error!.Value, opened.ErrorMessage);
		}

		var fileName = descriptor.Name.EndsWith(BackupFileNames.Extension, StringComparison.OrdinalIgnoreCase)
			? descriptor.Name
			: descriptor.Name + BackupFileNames.Extension;

		var handle = new BackupExportHandle(opened.Data!, fileName, descriptor.SizeBytes);
		return Result.Ok<BackupExportHandle, BackupError>(handle);
	}

	public async Task<Result<BackupError>> Delete(Guid backupId, CancellationToken cancellationToken = default)
	{
		var descriptor = await _catalog.Find(backupId, cancellationToken);
		if (descriptor is null)
		{
			return Result.Fail(BackupError.NotFound, "The backup was not found.");
		}

		var provider = _storageRegistry.Find(descriptor.ProviderId);
		if (provider is null)
		{
			return Result.Fail(BackupError.ProviderUnavailable, "The storage provider is not available.");
		}

		var deleteResult = await provider.Delete(descriptor.StorageId, cancellationToken);
		if (!deleteResult.Success)
		{
			return deleteResult;
		}

		await PublishListChanged("deleted", cancellationToken);

		return Result.Ok<BackupError>();
	}

	private async Task<Result<IBackupOperationLease, BackupError>> AcquireCreateLease(BackupTrigger trigger,
		CancellationToken cancellationToken)
	{
		// The two triggers that block something destructive wait up to a minute for the lease; every
		// other trigger only takes it if it is immediately free, since a caller that is not blocked on
		// the result can safely be represented by whatever backup is already running instead of queuing.
		return trigger is BackupTrigger.BeforeHostUpdate or BackupTrigger.BeforeRestore
			? await _gate.Acquire(BackupOperationKind.Create, trigger, _waitingTriggerTimeout, cancellationToken)
			: _gate.TryAcquire(BackupOperationKind.Create, trigger);
	}

	private Result<BackupDescriptor, BackupError> Fail(Guid operationId,
		BackupTrigger trigger,
		Guid backupId,
		BackupError error,
		string? errorMessage)
	{
		ReportStatus(operationId,
			BackupOperationStage.Failed,
			trigger,
			backupId,
			error: error,
			errorMessage: errorMessage);
		return Result.Fail<BackupDescriptor, BackupError>(error, errorMessage);
	}

	private void ReportStatus(Guid operationId,
		BackupOperationStage stage,
		BackupTrigger trigger,
		Guid? backupId,
		long? totalBytes = null,
		BackupError? error = null,
		string? errorMessage = null)
		=> _progress.Report(new BackupOperationStatus(operationId,
			BackupOperationKind.Create,
			stage,
			trigger,
			null,
			null,
			totalBytes,
			backupId,
			error,
			errorMessage,
			_time.GetUtcNow()));

	private async Task<Result<ResolvedSource, BackupError>> OpenSource(BackupSourceRef source,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(source.FilePath))
		{
			if (!File.Exists(source.FilePath))
			{
				return Result.Fail<ResolvedSource, BackupError>(BackupError.NotFound, "The file was not found.");
			}

			try
			{
				Stream stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				return Result.Ok<ResolvedSource, BackupError>(new ResolvedSource(stream, null));
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				return Result.Fail<ResolvedSource, BackupError>(BackupError.StorageFailure, e.Message);
			}
		}

		if (!string.IsNullOrWhiteSpace(source.ProviderId) && !string.IsNullOrWhiteSpace(source.StorageId))
		{
			var provider = _storageRegistry.Find(source.ProviderId);
			if (provider is null)
			{
				return Result.Fail<ResolvedSource, BackupError>(BackupError.ProviderUnavailable,
					"The storage provider is not available.");
			}

			var opened = await provider.OpenRead(source.StorageId, cancellationToken);
			if (!opened.Success)
			{
				return Result.Fail<ResolvedSource, BackupError>(opened.Error!.Value, opened.ErrorMessage);
			}

			return Result.Ok<ResolvedSource, BackupError>(new ResolvedSource(opened.Data!, provider));
		}

		return Result.Fail<ResolvedSource, BackupError>(BackupError.ValidationError,
			"A backup source must specify a file path or a provider and storage id.");
	}

	private async Task<IReadOnlyList<BackupComponentGroupInfo>> ReadComponentsFromPayload(Stream archive,
		byte[] key,
		BackupArchiveManifest manifest,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(_paths.RestoreStagingDirectory);
		var tempPath = Path.Combine(_paths.RestoreStagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");

		try
		{
			await _archiveReader.DecryptPayload(archive, key, tempPath, cancellationToken: cancellationToken);

			using var payload = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
			var entry = zip.GetEntry(BackupFileNames.ContentIndexEntry);
			if (entry is null)
			{
				return DeclaredComponents(manifest);
			}

			using var entryStream = entry.Open();
			var index = await JsonSerializer.DeserializeAsync<BackupContentIndex>(entryStream,
				PersistenceJsonOptions.Default,
				cancellationToken);

			if (index is null)
			{
				return DeclaredComponents(manifest);
			}

			var byId = index.Components.ToDictionary(component => component.Id);

			return
			[
				.. manifest.Components.Select(id => byId.TryGetValue(id, out var component)
					? new BackupComponentGroupInfo(id,
						component.EntryCount,
						component.ByteSize,
						BackupComponentGroups.Definition(id).Requires)
					: new BackupComponentGroupInfo(id, 0, 0, BackupComponentGroups.Definition(id).Requires))
			];
		}
		finally
		{
			TryDeleteFile(tempPath);
		}
	}

	private void MarkImported(Guid backupId)
	{
		var path = ImportedBackupRegistry.ResolvePath(_paths);
		var document = _importedStore.Read<ImportedBackupsDocument>(path, repair: false) ??
			new ImportedBackupsDocument();
		if (document.BackupIds.Contains(backupId))
		{
			return;
		}

		document.BackupIds.Add(backupId);
		_importedStore.Write(path, document);
	}

	private async Task PublishListChanged(string reason, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
		await publisher.Publish(new BackupListChangedNotification(reason), cancellationToken);
	}

	private static BackupArchiveManifest BuildManifest(Guid backupId,
		CreateBackupRequest request,
		Guid installationId,
		ReadOnlyMemory<byte> recoveryKey,
		string schemaVersion,
		DateTimeOffset createdAt)
		=> new()
		{
			BackupId = backupId,
			MacroDeckVersion = HostVersion.Current,
			CreatedAt = createdAt,
			Trigger = request.Trigger,
			InstallationId = installationId,
			RecoveryKeyId = BackupKeyDerivation.DeriveKeyId(recoveryKey.Span),
			Note = request.Note,
			HostPlatform = RuntimeInformation.RuntimeIdentifier,
			DatabaseSchemaVersion = schemaVersion,
			Components = [.. BackupComponentGroups.AllIds]
		};

	private static IReadOnlyList<BackupComponentGroupInfo> DeclaredComponents(BackupArchiveManifest manifest)
		=>
		[
			.. manifest.Components.Select(id =>
				new BackupComponentGroupInfo(id, 0, 0, BackupComponentGroups.Definition(id).Requires))
		];

	private static BackupError MapCryptoError(BackupDecryptResult result)
		=> result switch
		{
			BackupDecryptResult.WrongKey => BackupError.RecoveryKeyInvalid,
			BackupDecryptResult.Corrupt or BackupDecryptResult.Truncated => BackupError.IntegrityFailure,
			BackupDecryptResult.UnsupportedAlgorithm => BackupError.UnsupportedEncryption,
			BackupDecryptResult.TooLarge => BackupError.TooLarge,
			_ => BackupError.InvalidArchive
		};

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}

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

	private sealed record ResolvedSource(Stream Stream, IBackupStorageProvider? Provider);
}
