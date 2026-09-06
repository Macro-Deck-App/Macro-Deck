using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class IconImportService : IIconImportService
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IconImportBatchTracker _batchTracker;
	private readonly IconImportBatchFinalizer _batchFinalizer;
	private readonly IconProcessingChannel _processingChannel;
	private readonly IIconPackRestoreService _restoreService;
	private readonly IconImportCancellationRegistry _cancellationRegistry;
	private readonly IconImportCoalescer _coalescer;
	private readonly IAppIconExtractor _appIconExtractor;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	public IconImportService(
		IIconPackCache iconPackCache,
		IIconStorage storage,
		IconImportBatchTracker batchTracker,
		IconImportBatchFinalizer batchFinalizer,
		IconProcessingChannel processingChannel,
		IIconPackRestoreService restoreService,
		IconImportCancellationRegistry cancellationRegistry,
		IconImportCoalescer coalescer,
		IAppIconExtractor appIconExtractor,
		IMediator mediator,
		ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_batchTracker = batchTracker;
		_batchFinalizer = batchFinalizer;
		_processingChannel = processingChannel;
		_restoreService = restoreService;
		_cancellationRegistry = cancellationRegistry;
		_coalescer = coalescer;
		_appIconExtractor = appIconExtractor;
		_mediator = mediator;
		_logger = logger;
	}

	public async Task<Result<IconImportBatchEntity, IconError>> Import(Guid? packId,
		string? sourceName,
		IAsyncEnumerable<IconImportFile> files,
		CancellationToken cancellationToken)
	{
		var packResult = ResolveDestination(packId);
		if (!packResult.Success)
		{
			return Result.Fail<IconImportBatchEntity, IconError>(packResult.Error!.Value, packResult.ErrorMessage);
		}

		var pack = packResult.Data!;
		var batch = CreateBatch(pack.Id, sourceName, explicitDestination: packId is not null);
		_batchTracker.Register(batch);

		var stagedIcons = new List<IconEntity>();
		var duplicates = 0;
		var hasArchives = false;
		await foreach (var file in files.WithCancellation(cancellationToken))
		{
			if (IconImportFiles.IsArchive(file.FileName))
			{
				await _storage.StageArchive(batch.Id,
					Path.GetFileName(file.FileName),
					file.Content,
					cancellationToken);
				hasArchives = true;
			}
			else if (IconImportFiles.IsMacroDeckIconPack(file.FileName))
			{
				await MergePackArchive(batch, pack.Id, file.FileName, file.Content, cancellationToken);
			}
			else if (_appIconExtractor.CanExtractContent(file.FileName) &&
				!IconImportFiles.CarriesDirectory(file.FileName))
			{
				var outcome = await StageAppIconFromContent(batch, file.FileName, file.Content, cancellationToken);
				ApplyStageOutcome(outcome, batch, file.FileName, stagedIcons, ref duplicates);
			}
			else if (IconImportFiles.IsUploadedIcon(file.FileName))
			{
				var staged = await StageImage(batch, file.FileName, file.Content, cancellationToken);
				if (staged is null)
				{
					duplicates++;
				}
				else
				{
					stagedIcons.Add(staged);
				}
			}
			else
			{
				_logger.Debug("Skipping unsupported file {FileName} in import batch {BatchId}",
					file.FileName,
					batch.Id);
			}

			if (stagedIcons.Count >= 100)
			{
				await RegisterStagedIcons(batch, pack.Id, stagedIcons, cancellationToken);
				stagedIcons.Clear();
			}
		}

		await RegisterStagedIcons(batch, pack.Id, stagedIcons, cancellationToken);
		RecordDuplicatesSkipped(batch, pack, duplicates);

		if (hasArchives)
		{
			_processingChannel.Enqueue(new ExtractBatchWorkItem(batch.Id));
		}
		else
		{
			var (total, _, _) = _batchTracker.GetCounters(batch.Id);
			await FinishDiscovery(batch, total, cancellationToken);
		}

		return Result.Ok<IconImportBatchEntity, IconError>(batch);
	}

	public async Task<Result<IconImportBatchEntity, IconError>> ImportFromPath(Guid? packId,
		IReadOnlyList<string> paths,
		CancellationToken cancellationToken)
	{
		if (paths is null || paths.Count == 0)
		{
			return Result.Fail<IconImportBatchEntity, IconError>(IconError.ValidationError, "No path given");
		}

		var packResult = ResolveDestination(packId);
		if (!packResult.Success)
		{
			return Result.Fail<IconImportBatchEntity, IconError>(packResult.Error!.Value, packResult.ErrorMessage);
		}

		var sourceFiles = new List<(string RelativeName, string FullPath)>();
		var resolved = 0;
		var unreadable = 0;
		var skippedFolders = 0;
		foreach (var path in paths)
		{
			try
			{
				if (File.Exists(path))
				{
					sourceFiles.Add((Path.GetFileName(path), path));
					resolved++;
				}
				else if (Directory.Exists(path))
				{
					if (IconImportFiles.IsAppIconSource(path))
					{
						var trimmed = IconImportFiles.TrimTrailingSeparators(path);
						sourceFiles.Add((Path.GetFileName(trimmed), path));
					}
					else
					{
						var root = Path.GetFullPath(path);
						var swept = SweepImportFolder(root, out var sweepSkipped);
						skippedFolders += sweepSkipped;
						sourceFiles.AddRange(swept.Order().Select(f => (Path.GetRelativePath(root, f), f)));
					}

					resolved++;
				}
				else
				{
					unreadable++;
					_logger.Warning("Skipping {Path} for import: it does not exist", path);
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// A folder that turns out to be unreadable (permissions, a disconnected volume)
				// must not take the other seven the user dropped down with it.
				unreadable++;
				_logger.Warning(ex, "Skipping {Path} for import: it could not be read", path);
			}
		}

		if (resolved == 0)
		{
			return Result.Fail<IconImportBatchEntity, IconError>(IconError.ValidationError, "Path does not exist");
		}

		var pack = packResult.Data!;
		var batch = CreateBatch(pack.Id,
			BatchSourceName(paths),
			explicitDestination: packId is not null);
		if (unreadable > 0)
		{
			batch.Error = unreadable == 1
				? "1 dropped item could not be read and was skipped."
				: $"{unreadable} dropped items could not be read and were skipped.";
		}

		if (skippedFolders > 0)
		{
			AppendBatchError(batch,
				skippedFolders == 1
					? "1 folder inside could not be read and was skipped."
					: $"{skippedFolders} folders inside could not be read and were skipped.");
		}

		_batchTracker.Register(batch);

		var stagedIcons = new List<IconEntity>();
		var duplicates = 0;
		var hasArchives = false;
		foreach (var (relativeName, fullPath) in sourceFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				if (IconImportFiles.IsAppIconSource(relativeName))
				{
					var outcome = await StageAppIcon(batch, relativeName, fullPath, cancellationToken);
					ApplyStageOutcome(outcome, batch, fullPath, stagedIcons, ref duplicates);
				}
				else
				{
					await using var stream = File.OpenRead(fullPath);
					if (IconImportFiles.IsArchive(relativeName))
					{
						await _storage.StageArchive(batch.Id,
							Path.GetFileName(relativeName),
							stream,
							cancellationToken);
						hasArchives = true;
					}
					else if (IconImportFiles.IsMacroDeckIconPack(relativeName))
					{
						await MergePackArchive(batch, pack.Id, relativeName, stream, cancellationToken);
					}
					else if (IconImportFiles.IsSupportedImportEntry(relativeName))
					{
						var staged = await StageImage(batch, relativeName, stream, cancellationToken);
						if (staged is null)
						{
							duplicates++;
						}
						else
						{
							stagedIcons.Add(staged);
						}
					}
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Failed to stage {Path} for import batch {BatchId}", fullPath, batch.Id);
			}

			if (stagedIcons.Count >= 100)
			{
				await RegisterStagedIcons(batch, pack.Id, stagedIcons, cancellationToken);
				stagedIcons.Clear();
			}
		}

		await RegisterStagedIcons(batch, pack.Id, stagedIcons, cancellationToken);
		RecordDuplicatesSkipped(batch, pack, duplicates);

		if (hasArchives)
		{
			_processingChannel.Enqueue(new ExtractBatchWorkItem(batch.Id));
		}
		else
		{
			var (total, _, _) = _batchTracker.GetCounters(batch.Id);
			await FinishDiscovery(batch, total, cancellationToken);
		}

		return Result.Ok<IconImportBatchEntity, IconError>(batch);
	}

	public async Task<Result<SingleIconImportResult, IconError>> ImportSingle(Guid? packId,
		IconImportFile file,
		CancellationToken cancellationToken)
	{
		if (!IconImportFiles.IsSupportedImportEntry(file.FileName))
		{
			return Result.Fail<SingleIconImportResult, IconError>(IconError.UnsupportedFormat,
				$"{Path.GetFileName(file.FileName)} is not a supported image");
		}

		var packResult = ResolveDestination(packId);
		if (!packResult.Success)
		{
			return Result.Fail<SingleIconImportResult, IconError>(packResult.Error!.Value, packResult.ErrorMessage);
		}

		var pack = packResult.Data!;
		var batch = CreateBatch(pack.Id,
			Path.GetFileName(file.FileName),
			explicitDestination: packId is not null,
			silent: true);

		var iconId = Guid.CreateVersion7();
		var scope = packId is null ? IconImportCoalescer.CatalogWideScope : pack.Id;
		var sourceHash = await _storage.StageOriginal(batch.Id, iconId, file.FileName, file.Content, cancellationToken);
		var existing = FindReusableIcon(scope, sourceHash, iconId);
		if (existing is not null)
		{
			_storage.CleanupBatchStaging(batch.Id);
			_logger.Debug("Reusing icon {IconId} for {FileName}: identical content is already imported",
				existing.Id,
				file.FileName);
			return Result.Ok<SingleIconImportResult, IconError>(new SingleIconImportResult(existing, Reused: true));
		}

		_batchTracker.Register(batch);
		var icon = NewStagedIcon(batch, iconId, file.FileName, sourceHash);
		await RegisterStagedIcons(batch, pack.Id, [icon], cancellationToken);
		var (total, _, _) = _batchTracker.GetCounters(batch.Id);
		await FinishDiscovery(batch, total, cancellationToken);

		return Result.Ok<SingleIconImportResult, IconError>(new SingleIconImportResult(icon, Reused: false));
	}

	public async Task<Result<IconPackImportResult, IconError>> ImportPacks(IAsyncEnumerable<IconImportFile> files,
		CancellationToken cancellationToken)
	{
		var restoredPacks = new List<IconPackEntity>();
		IconImportBatchEntity? batch = null;
		string? firstError = null;

		await foreach (var file in files.WithCancellation(cancellationToken))
		{
			if (IconImportFiles.IsMacroDeckIconPack(file.FileName))
			{
				var result = await _restoreService.RestoreAsNewPack(file.FileName, file.Content, cancellationToken);
				if (result.Success)
				{
					restoredPacks.Add(result.Data!);
				}
				else
				{
					firstError ??= $"{Path.GetFileName(file.FileName)}: " +
						(result.ErrorMessage ?? result.Error!.Value.ToString());
					_logger.Warning("Failed to restore icon pack {FileName}: {Error}",
						file.FileName,
						result.ErrorMessage ?? result.Error!.Value.ToString());
				}
			}
			else if (IconImportFiles.IsArchive(file.FileName))
			{
				if (batch is null)
				{
					var defaultPack = _iconPackCache.GetDefaultPack();
					if (defaultPack is null)
					{
						return Result.Fail<IconPackImportResult, IconError>(IconError.PackNotFound);
					}

					batch = CreateBatch(defaultPack.Id,
						Path.GetFileName(file.FileName),
						explicitDestination: false,
						IconImportMode.NewPacks);
					_batchTracker.Register(batch);
				}

				await _storage.StageArchive(batch.Id,
					Path.GetFileName(file.FileName),
					file.Content,
					cancellationToken);
			}
			else
			{
				_logger.Debug("Skipping unsupported file {FileName} in pack import", file.FileName);
			}
		}

		if (batch is not null)
		{
			_processingChannel.Enqueue(new ExtractBatchWorkItem(batch.Id));
		}

		if (restoredPacks.Count == 0 && batch is null)
		{
			return firstError is not null
				? Result.Fail<IconPackImportResult, IconError>(IconError.InvalidArchive, firstError)
				: Result.Fail<IconPackImportResult, IconError>(IconError.ValidationError,
					"No supported icon pack files were provided");
		}

		return Result.Ok<IconPackImportResult, IconError>(new IconPackImportResult(batch, restoredPacks));
	}

	public async Task<bool> CancelBatch(Guid batchId, CancellationToken cancellationToken)
	{
		_cancellationRegistry.Cancel(batchId);

		var (total, _, failed) = _batchTracker.GetCounters(batchId);
		var batch = _batchTracker.TryFinish(batchId);
		if (batch is null)
		{
			return false;
		}

		batch.State = IconImportBatchState.Cancelled;
		batch.Total = total;

		var unfinished = _iconPackCache
			.GetIconsByBatchId(batchId)
			.Where(icon => icon.ProcessingState != IconProcessingState.Ready)
			.ToList();
		foreach (var packGroup in unfinished.GroupBy(icon => icon.PackId))
		{
			await _iconPackCache.RemoveIcons(packGroup.Key, packGroup.Select(icon => icon.Id).ToList());
			foreach (var icon in packGroup)
			{
				_coalescer.ReleaseAll(icon.Id);
				_storage.DeleteIconFiles(icon.PackId, icon.Id);
				await _mediator.Publish(new IconDeletedNotification(icon.Id, icon.PackId), cancellationToken);
			}
		}

		await _iconPackCache.FlushPendingWrites();

		var kept = _iconPackCache
			.GetIconsByBatchId(batchId)
			.Count(icon => icon.ProcessingState == IconProcessingState.Ready);

		await _mediator.Publish(new IconImportProgressNotification(batch, total, kept, failed),
			cancellationToken);
		_storage.CleanupBatchStaging(batchId);
		_cancellationRegistry.Release(batchId);

		_logger.Information("Icon import batch {BatchId} cancelled: {Kept} kept, {Removed} removed",
			batchId,
			kept,
			unfinished.Count);
		return true;
	}

	private async Task MergePackArchive(IconImportBatchEntity batch,
		Guid packId,
		string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var result = await _restoreService.MergeIntoPack(packId, batch.Id, fileName, content, cancellationToken);
		if (!result.Success)
		{
			batch.Error = $"Failed to import {Path.GetFileName(fileName)}: " +
				(result.ErrorMessage ?? result.Error!.Value.ToString());
			_logger.Warning("Failed to merge icon pack {FileName} into pack {PackId}: {Error}",
				fileName,
				packId,
				batch.Error);
			return;
		}

		var icons = result.Data!;
		_batchTracker.AddToTotal(batch.Id, icons.Count);
		_batchTracker.AddProcessed(batch.Id, icons.Count);
		await _mediator.Publish(new IconsAddedNotification(batch.Id, packId, icons), cancellationToken);
	}

	private Result<IconPackEntity, IconError> ResolveDestination(Guid? packId)
	{
		var pack = packId is null ? _iconPackCache.GetDefaultPack() : _iconPackCache.GetPackById(packId.Value);
		if (pack is null)
		{
			return Result.Fail<IconPackEntity, IconError>(IconError.PackNotFound);
		}

		if (pack.IsReadOnly)
		{
			return Result.Fail<IconPackEntity, IconError>(IconError.PackReadOnly);
		}

		return Result.Ok<IconPackEntity, IconError>(pack);
	}

	private static string? BatchSourceName(IReadOnlyList<string> paths)
		=> paths.Count == 1
			? Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar))
			: $"{paths.Count} items";

	private static IconImportBatchEntity CreateBatch(Guid packId,
		string? sourceName,
		bool explicitDestination,
		IconImportMode mode = IconImportMode.Destination,
		bool silent = false)
		=> new()
		{
			Id = Guid.CreateVersion7(),
			PackId = packId,
			SourceName = sourceName,
			State = IconImportBatchState.Discovering,
			ExplicitDestination = explicitDestination,
			Mode = mode,
			Silent = silent,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};

	private List<string> SweepImportFolder(string root, out int skipped)
	{
		var results = new List<string>();
		var skippedCount = 0;
		var stack = new Stack<string>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			var directory = stack.Pop();
			List<string> subdirectories;
			List<string> files;
			try
			{
				subdirectories = Directory.EnumerateDirectories(directory).ToList();
				files = Directory.EnumerateFiles(directory).ToList();
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// An unreadable dropped folder is one the caller reports as a dropped item; a subdirectory
				// inside it must not take the rest of the folder down with it, and is counted separately
				// so the caller never reports it as something the user dropped.
				if (directory == root)
				{
					throw;
				}

				skippedCount++;
				_logger.Warning(ex, "Skipping {Directory} for import: it could not be read", directory);
				continue;
			}

			foreach (var subdirectory in subdirectories)
			{
				if (IconImportFiles.IsAppIconSource(subdirectory))
				{
					// A .app bundle is a leaf: never descend into it looking for more icons.
					results.Add(subdirectory);
				}
				else
				{
					stack.Push(subdirectory);
				}
			}

			foreach (var file in files)
			{
				if (IconImportFiles.IsFolderImportEntry(file))
				{
					results.Add(file);
				}
			}
		}

		skipped = skippedCount;
		return results;
	}

	private async Task<StagedOutcome> StageAppIcon(IconImportBatchEntity batch,
		string relativeName,
		string fullPath,
		CancellationToken cancellationToken)
	{
		var extraction = await _appIconExtractor.Extract(fullPath, cancellationToken);
		if (!extraction.Success)
		{
			return StagedOutcome.Failed(extraction.ErrorMessage ?? extraction.Error!.Value.ToString());
		}

		var extracted = extraction.Data!;
		var relativeDirectory = Path.GetDirectoryName(relativeName);
		var fileName = string.IsNullOrEmpty(relativeDirectory)
			? extracted.FileName
			: Path.Combine(relativeDirectory, extracted.FileName);

		await using var content = new MemoryStream(extracted.Content);
		var icon = await StageImage(batch, fileName, content, cancellationToken);
		return icon is null ? StagedOutcome.AsDuplicate() : StagedOutcome.Staged(icon);
	}

	private async Task<StagedOutcome> StageAppIconFromContent(IconImportBatchEntity batch,
		string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var extraction = await _appIconExtractor.ExtractFromContent(fileName, content, cancellationToken);
		if (!extraction.Success)
		{
			return StagedOutcome.Failed(extraction.ErrorMessage ?? extraction.Error!.Value.ToString());
		}

		var extracted = extraction.Data!;
		await using var extractedContent = new MemoryStream(extracted.Content);
		var icon = await StageImage(batch, extracted.FileName, extractedContent, cancellationToken);
		return icon is null ? StagedOutcome.AsDuplicate() : StagedOutcome.Staged(icon);
	}

	private static void ApplyStageOutcome(StagedOutcome outcome,
		IconImportBatchEntity batch,
		string source,
		List<IconEntity> stagedIcons,
		ref int duplicates)
	{
		if (outcome.Icon is not null)
		{
			stagedIcons.Add(outcome.Icon);
		}
		else if (outcome.IsDuplicate)
		{
			duplicates++;
		}
		else
		{
			AppendExtractionFailure(batch, source, outcome.FailureMessage);
		}
	}

	private static void AppendExtractionFailure(IconImportBatchEntity batch, string source, string? reason)
	{
		var name = Path.GetFileName(IconImportFiles.TrimTrailingSeparators(source));
		AppendBatchError(batch, reason is null ? $"{name} could not be read." : $"{name} could not be read: {reason}");
	}

	private static void AppendBatchError(IconImportBatchEntity batch, string message)
		=> batch.Error = batch.Error is null ? message : $"{batch.Error} {message}";

	private readonly record struct StagedOutcome(IconEntity? Icon, bool IsDuplicate, string? FailureMessage)
	{
		public static StagedOutcome Staged(IconEntity icon) => new(icon, IsDuplicate: false, FailureMessage: null);

		public static StagedOutcome AsDuplicate() => new(Icon: null, IsDuplicate: true, FailureMessage: null);

		public static StagedOutcome Failed(string? message) => new(Icon: null, IsDuplicate: false, message);
	}

	private async Task<IconEntity?> StageImage(IconImportBatchEntity batch,
		string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var iconId = Guid.CreateVersion7();
		var sourceHash = await _storage.StageOriginal(batch.Id, iconId, fileName, content, cancellationToken);

		// Scoped to the destination pack: a pack import populates the pack the user chose, so an identical
		// icon sitting in some other pack must not stand in for the item they asked for. Losing the
		// reservation is enough to skip - unlike the single-icon path this caller has no use for the
		// winning entity, only for the fact that something else is already importing this content.
		if (_iconPackCache.FindBySourceContentHash(sourceHash, batch.PackId) is not null ||
			_coalescer.TryReserve(batch.PackId, sourceHash, iconId) is not null)
		{
			_storage.DeleteStagedOriginal(batch.Id, iconId);
			return null;
		}

		return NewStagedIcon(batch, iconId, fileName, sourceHash);
	}

	private static IconEntity NewStagedIcon(IconImportBatchEntity batch,
		Guid iconId,
		string fileName,
		SourceContentHash sourceHash)
		=> new()
		{
			Id = iconId,
			PackId = batch.PackId,
			Name = Path.GetFileNameWithoutExtension(fileName),
			SourceContentHash = sourceHash.Value,
			OriginalFileName = fileName,
			ProcessingState = IconProcessingState.Pending,
			ImportBatchId = batch.Id,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};

	private IconEntity? FindReusableIcon(Guid scope, SourceContentHash sourceHash, Guid iconId)
	{
		var withinPackId = scope == IconImportCoalescer.CatalogWideScope ? (Guid?)null : scope;
		var ready = _iconPackCache.FindBySourceContentHash(sourceHash, withinPackId);
		if (ready is not null)
		{
			return ready;
		}

		var winner = _coalescer.TryReserve(scope, sourceHash, iconId);
		if (winner is null)
		{
			return null;
		}

		return _iconPackCache.GetIconById(winner.Value);
	}

	private void RecordDuplicatesSkipped(IconImportBatchEntity batch, IconPackEntity pack, int duplicates)
	{
		if (duplicates == 0)
		{
			return;
		}

		batch.Skipped += duplicates;
		_batchTracker.Persist(batch);
		_logger.Information(
			"Skipped {Count} file(s) in import batch {BatchId}: {PackName} already holds that exact content",
			duplicates,
			batch.Id,
			pack.Name);
	}

	private async Task RegisterStagedIcons(IconImportBatchEntity batch,
		Guid packId,
		List<IconEntity> icons,
		CancellationToken cancellationToken)
	{
		if (icons.Count == 0)
		{
			return;
		}

		await _iconPackCache.AddIcons(packId, icons);
		_batchTracker.AddToTotal(batch.Id, icons.Count);
		await _mediator.Publish(new IconsAddedNotification(batch.Id, packId, icons.ToList()), cancellationToken);
		foreach (var icon in icons)
		{
			_processingChannel.Enqueue(new ProcessIconWorkItem(icon.Id));
		}
	}

	private async Task FinishDiscovery(IconImportBatchEntity batch, int total, CancellationToken cancellationToken)
	{
		batch.State = IconImportBatchState.Processing;
		batch.Total = total;
		_batchTracker.Persist(batch);
		var (_, processed, failed) = _batchTracker.GetCounters(batch.Id);
		await _mediator.Publish(new IconImportProgressNotification(batch, total, processed, failed),
			cancellationToken);
		await _batchFinalizer.TryFinalize(batch.Id, _mediator, cancellationToken);
	}
}
