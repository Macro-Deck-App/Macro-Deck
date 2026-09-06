using System.IO.Compression;
using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public class IconProcessingBackgroundService : HostReadyBackgroundService
{
	private const int ExtractChunkSize = 100;
	private const int MaxArchiveEntries = 10_000;

	private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _inFlightIcons = new();
	private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _inFlightExtractions = new();

	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IIconProcessor _processor;
	private readonly IconImportBatchTracker _batchTracker;
	private readonly IconImportBatchFinalizer _batchFinalizer;
	private readonly IconProcessingChannel _processingChannel;
	private readonly IconImportCancellationRegistry _cancellationRegistry;
	private readonly IconImportCoalescer _coalescer;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	public IconProcessingBackgroundService(
		IHostApplicationLifetime lifetime,
		IIconPackCache iconPackCache,
		IIconStorage storage,
		IIconProcessor processor,
		IconImportBatchTracker batchTracker,
		IconImportBatchFinalizer batchFinalizer,
		IconProcessingChannel processingChannel,
		IconImportCancellationRegistry cancellationRegistry,
		IconImportCoalescer coalescer,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
		: base(lifetime)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_processor = processor;
		_batchTracker = batchTracker;
		_batchFinalizer = batchFinalizer;
		_processingChannel = processingChannel;
		_cancellationRegistry = cancellationRegistry;
		_coalescer = coalescer;
		_scopeFactory = scopeFactory;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _iconPackCache.InitializeCache();
		await RecoverInterruptedImports(stoppingToken);

		var workerCount = Math.Clamp(Environment.ProcessorCount - 1, 2, 4);
		var workers = Enumerable
			.Range(0, workerCount)
			.Select(_ => Task.Run(() => WorkerLoop(stoppingToken), stoppingToken))
			.ToList();
		await Task.WhenAll(workers);
	}

	private async Task WorkerLoop(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var item in _processingChannel.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await using var scope = _scopeFactory.CreateAsyncScope();
					var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
					switch (item)
					{
						case ProcessIconWorkItem processItem:
							await ProcessIcon(processItem.IconId, mediator, stoppingToken);
							break;
						case ExtractBatchWorkItem extractItem:
							using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken,
								_cancellationRegistry.TokenFor(extractItem.BatchId)))
							{
								await ExtractBatch(extractItem.BatchId, mediator, linkedCts.Token);
							}

							break;
					}
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					throw;
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Icon work item {WorkItem} failed", item);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Graceful shutdown; in-flight items are recovered on the next start.
		}
	}


	private async Task ProcessIcon(Guid iconId, IMediator mediator, CancellationToken cancellationToken)
	{
		if (!_inFlightIcons.TryAdd(iconId, 0))
		{
			return;
		}

		try
		{
			var icon = _iconPackCache.GetIconById(iconId);
			if (icon is null ||
				icon.ProcessingState is not IconProcessingState.Pending
					and not IconProcessingState.Processing ||
				icon.ImportBatchId is null)
			{
				return;
			}

			var batchId = icon.ImportBatchId.Value;
			icon.ProcessingState = IconProcessingState.Processing;
			await _iconPackCache.UpdateIcon(icon);

			var staged = _storage.OpenStagedOriginal(batchId, icon.Id);
			if (staged is null)
			{
				await MarkFailed(icon, "Original file was lost", mediator, cancellationToken);
			}
			else
			{
				using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
					_cancellationRegistry.TokenFor(batchId));
				await using (staged)
				{
					var result = await _processor.Process(staged,
						icon.OriginalFileName ?? icon.Name,
						linkedCts.Token);
					if (result.Success)
					{
						await StoreProcessedIcon(icon, result.Data!, mediator, cancellationToken);
					}
					else
					{
						await MarkFailed(icon,
							result.ErrorMessage ?? result.Error?.ToString() ?? "Processing failed",
							mediator,
							cancellationToken);
					}
				}
			}

			await ReportProgress(batchId, mediator, force: false, cancellationToken);
			await _batchFinalizer.TryFinalize(batchId, mediator, cancellationToken);
		}
		finally
		{
			_coalescer.ReleaseAll(iconId);
			_inFlightIcons.TryRemove(iconId, out _);
		}
	}

	private async Task StoreProcessedIcon(IconEntity icon,
		ProcessedIconResult processed,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		foreach (var (size, bytes) in processed.Variants.OrderBy(v => v.Key))
		{
			await _storage.WriteVariant(icon.PackId,
				icon.Id,
				size.ToString(System.Globalization.CultureInfo.InvariantCulture),
				bytes,
				cancellationToken);
		}

		await _storage.WriteVariant(icon.PackId,
			icon.Id,
			IconVariants.Master,
			processed.MasterWebp,
			cancellationToken);

		icon.Width = processed.Width;
		icon.Height = processed.Height;
		icon.IsAnimated = processed.IsAnimated;
		icon.FrameCount = processed.FrameCount;
		if (icon.SourceContentHash is not null && icon.SourceContentHash != processed.SourceContentHash.Value)
		{
			_logger.Warning("Source hash of icon {IconId} changed between staging and processing", icon.Id);
		}

		icon.SourceContentHash = processed.SourceContentHash.Value;
		icon.MasterContentHash = processed.MasterContentHash.Value;
		icon.OriginalFormat = processed.OriginalFormat;
		icon.AvailableSizes = processed.Variants.Keys.OrderBy(s => s).ToList();
		icon.ProcessingState = IconProcessingState.Ready;
		icon.ProcessingError = null;
		await _iconPackCache.UpdateIcon(icon);

		if (icon.ImportBatchId is not null)
		{
			_batchTracker.IncrementProcessed(icon.ImportBatchId.Value);
		}

		await mediator.Publish(new IconUpdatedNotification(icon), cancellationToken);
	}

	private async Task MarkFailed(IconEntity icon,
		string error,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		icon.ProcessingState = IconProcessingState.Failed;
		icon.ProcessingError = error;
		await _iconPackCache.UpdateIcon(icon);

		if (icon.ImportBatchId is not null)
		{
			_batchTracker.IncrementFailed(icon.ImportBatchId.Value);
		}

		_logger.Warning("Icon {IconName} ({IconId}) failed to process: {Error}", icon.Name, icon.Id, error);
		await mediator.Publish(new IconUpdatedNotification(icon), cancellationToken);
	}


	private async Task ExtractBatch(Guid batchId, IMediator mediator, CancellationToken cancellationToken)
	{
		if (!_inFlightExtractions.TryAdd(batchId, 0))
		{
			return;
		}

		try
		{
			await ExtractBatchCore(batchId, mediator, cancellationToken);
		}
		finally
		{
			_inFlightExtractions.TryRemove(batchId, out _);
		}
	}

	private async Task ExtractBatchCore(Guid batchId, IMediator mediator, CancellationToken cancellationToken)
	{
		var batch = _batchTracker.Get(batchId);
		if (batch is null || batch.State != IconImportBatchState.Discovering)
		{
			return;
		}

		if (_iconPackCache.GetPackById(batch.PackId) is null)
		{
			_logger.Warning("Abandoning import batch {BatchId}: destination pack no longer exists", batchId);
			_batchTracker.TryFinish(batchId);
			_storage.CleanupBatchStaging(batchId);
			return;
		}

		var archivePaths = _storage.GetStagedArchivePaths(batchId);
		if (batch.Mode == IconImportMode.Destination)
		{
			await AdoptStreamDeckPackMetadata(batch, archivePaths, mediator, cancellationToken);
		}

		// Re-extraction after a crash must not duplicate icons that were already committed; entries
		// are keyed by "<archive>/<entry path>" in OriginalFileName.
		var existingKeys = _iconPackCache
			.GetIconsByBatchId(batchId)
			.Select(i => i.OriginalFileName)
			.OfType<string>()
			.ToHashSet(StringComparer.Ordinal);

		foreach (var archivePath in archivePaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				await ExtractArchive(batch, archivePath, existingKeys, mediator, cancellationToken);
				File.Delete(archivePath);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				batch.Error = $"Failed to extract {DisplayNameFor(archivePath)}: {ex.Message}";
				_logger.Warning(ex,
					"Failed to extract archive {ArchivePath} for batch {BatchId}",
					archivePath,
					batchId);
			}
		}

		var (total, processed, failed) = _batchTracker.GetCounters(batchId);
		batch.State = total == 0 && batch.Error is not null
			? IconImportBatchState.Failed
			: IconImportBatchState.Processing;
		batch.Total = total;
		_batchTracker.Persist(batch);

		if (batch.State == IconImportBatchState.Failed)
		{
			if (_batchTracker.TryFinish(batchId) is not null)
			{
				await mediator.Publish(new IconImportProgressNotification(batch, total, processed, failed),
					cancellationToken);
				_storage.CleanupBatchStaging(batchId);
			}

			return;
		}

		await ReportProgress(batchId, mediator, force: true, cancellationToken);
		await _batchFinalizer.TryFinalize(batchId, mediator, cancellationToken);
	}

	private async Task ExtractArchive(IconImportBatchEntity batch,
		string archivePath,
		HashSet<string> existingKeys,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		var archiveDisplayName = DisplayNameFor(archivePath);
		await using var archive = await ZipFile.OpenReadAsync(archivePath, cancellationToken);

		var iconNames = IconImportFiles.IsStreamDeckIconPack(archivePath)
			? ParseStreamDeckIconNames(archive)
			: null;
		var packResolver = batch.Mode == IconImportMode.NewPacks
			? CreateNewPacksResolver(batch, archive, archivePath, archiveDisplayName, mediator)
			: null;

		var chunk = new List<IconEntity>();
		var entryCount = 0;
		foreach (var entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry.Name.Length == 0 || !IconImportFiles.IsSupportedImportEntry(entry.FullName))
			{
				continue;
			}

			if (++entryCount > MaxArchiveEntries)
			{
				batch.Error = $"{archiveDisplayName} contains more than {MaxArchiveEntries} images";
				_logger.Warning("Archive {Archive} exceeds the entry limit; remaining entries are skipped",
					archiveDisplayName);
				break;
			}

			var key = $"{archiveDisplayName}/{entry.FullName}";
			if (existingKeys.Contains(key))
			{
				continue;
			}

			var packId = packResolver is null ? batch.PackId : await packResolver(entry, cancellationToken);
			var icon = new IconEntity
			{
				Id = Guid.CreateVersion7(),
				PackId = packId,
				Name = iconNames?.GetValueOrDefault(Path.GetFileName(entry.FullName)) ??
					Path.GetFileNameWithoutExtension(entry.Name),
				OriginalFileName = key,
				ProcessingState = IconProcessingState.Pending,
				ImportBatchId = batch.Id,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			};

			SourceContentHash sourceHash;
			await using (var entryStream = await entry.OpenAsync(cancellationToken))
			{
				sourceHash = await _storage.StageOriginal(batch.Id,
					icon.Id,
					entry.Name,
					entryStream,
					cancellationToken);
			}

			if (_iconPackCache.FindBySourceContentHash(sourceHash, packId) is not null ||
				_coalescer.TryReserve(packId, sourceHash, icon.Id) is not null)
			{
				_storage.DeleteStagedOriginal(batch.Id, icon.Id);
				_logger.Debug("Skipping archive entry {Entry}: pack {PackId} already holds that content", key, packId);
				batch.Skipped++;
				existingKeys.Add(key);
				continue;
			}

			icon.SourceContentHash = sourceHash.Value;
			chunk.Add(icon);
			existingKeys.Add(key);

			if (chunk.Count >= ExtractChunkSize)
			{
				await CommitChunk(batch, chunk, mediator, cancellationToken);
			}
		}

		await CommitChunk(batch, chunk, mediator, cancellationToken);
	}

	private async Task CommitChunk(IconImportBatchEntity batch,
		List<IconEntity> chunk,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		if (chunk.Count == 0)
		{
			return;
		}

		var icons = chunk.ToList();
		chunk.Clear();

		foreach (var group in icons.GroupBy(icon => icon.PackId))
		{
			var packIcons = group.ToList();
			await _iconPackCache.AddIcons(group.Key, packIcons);
			await mediator.Publish(new IconsAddedNotification(batch.Id, group.Key, packIcons), cancellationToken);
		}

		_batchTracker.AddToTotal(batch.Id, icons.Count);
		foreach (var icon in icons)
		{
			_processingChannel.Enqueue(new ProcessIconWorkItem(icon.Id));
		}

		await ReportProgress(batch.Id, mediator, force: false, cancellationToken);
	}


	private Func<ZipArchiveEntry, CancellationToken, Task<Guid>> CreateNewPacksResolver(
		IconImportBatchEntity batch,
		ZipArchive archive,
		string archivePath,
		string archiveDisplayName,
		IMediator mediator)
	{
		var fallbackName = Path.GetFileNameWithoutExtension(archiveDisplayName);
		var resolvedPacks = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
		if (IconImportFiles.IsTouchPortalIconPack(archivePath))
		{
			var folderInfos = ParseTouchPortalFolderInfos(archive);
			return async (entry, cancellationToken) =>
			{
				var topFolder = TopLevelFolderOf(entry);
				if (topFolder is null || !folderInfos.TryGetValue(topFolder, out var info))
				{
					return await ResolvePack(string.Empty,
						fallbackName,
						author: null,
						description: null,
						version: null,
						IconPackSourceType.TouchPortalImport,
						cancellationToken);
				}

				return await ResolvePack(topFolder,
					info.Name ?? topFolder,
					info.Author,
					info.Link,
					version: null,
					IconPackSourceType.TouchPortalImport,
					cancellationToken);
			};
		}

		if (IconImportFiles.IsStreamDeckIconPack(archivePath))
		{
			var metadata = TryParseStreamDeckManifest(archive, batch.Id);
			return (_, cancellationToken) => ResolvePack(string.Empty,
				metadata.Name ?? fallbackName,
				metadata.Author,
				description: null,
				metadata.Version,
				IconPackSourceType.StreamDeckImport,
				cancellationToken);
		}

		return (_, cancellationToken) => ResolvePack(string.Empty,
			fallbackName,
			author: null,
			description: null,
			version: null,
			IconPackSourceType.User,
			cancellationToken);

		async Task<Guid> ResolvePack(string localKey,
			string name,
			string? author,
			string? description,
			string? version,
			IconPackSourceType sourceType,
			CancellationToken cancellationToken)
		{
			if (resolvedPacks.TryGetValue(localKey, out var cached))
			{
				return cached;
			}

			var sourceId = localKey.Length == 0
				? $"import:{batch.Id:N}:{archiveDisplayName}"
				: $"import:{batch.Id:N}:{archiveDisplayName}:{localKey}";
			var pack = await ResolveOrCreateImportPack(batch,
				sourceId,
				name,
				author,
				description,
				version,
				sourceType,
				mediator,
				cancellationToken);
			resolvedPacks[localKey] = pack.Id;
			return pack.Id;
		}
	}

	private async Task<IconPackEntity> ResolveOrCreateImportPack(IconImportBatchEntity batch,
		string sourceId,
		string name,
		string? author,
		string? description,
		string? version,
		IconPackSourceType sourceType,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		var pack = _iconPackCache.GetAllPacks().FirstOrDefault(p => p.SourceId == sourceId);
		if (pack is null)
		{
			pack = new IconPackEntity
			{
				Id = Guid.CreateVersion7(),
				Name = name,
				Author = author,
				Description = description,
				Version = version,
				SourceType = sourceType,
				SourceId = sourceId,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			};
			await _iconPackCache.AddOrUpdatePack(pack);
			await mediator.Publish(new IconPackCreatedNotification(pack, 0), cancellationToken);
		}

		var batchSourcePrefix = $"import:{batch.Id:N}:";
		var primaryPack = _iconPackCache.GetPackById(batch.PackId);
		if (primaryPack?.SourceId?.StartsWith(batchSourcePrefix, StringComparison.Ordinal) is not true)
		{
			batch.PackId = pack.Id;
			_batchTracker.Persist(batch);
		}

		return pack;
	}

	private (string? Name, string? Author, string? Version) TryParseStreamDeckManifest(ZipArchive archive,
		Guid batchId)
	{
		try
		{
			return ParseStreamDeckManifest(archive);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to read .streamDeckIconPack manifest for batch {BatchId}", batchId);
			return (null, null, null);
		}
	}

	private static Dictionary<string, TouchPortalPackInfo> ParseTouchPortalFolderInfos(ZipArchive archive)
	{
		var infos = new Dictionary<string, TouchPortalPackInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in archive.Entries)
		{
			var segments = entry.FullName.Replace('\\', '/').Split('/');
			if (segments.Length != 2 || !segments[1].Equals("info.txt", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			using var stream = entry.Open();
			infos[segments[0]] = TouchPortalInfoParser.Parse(stream);
		}

		return infos;
	}

	private static string? TopLevelFolderOf(ZipArchiveEntry entry)
	{
		var normalized = entry.FullName.Replace('\\', '/');
		var separator = normalized.IndexOf('/');
		return separator > 0 ? normalized[..separator] : null;
	}

	private async Task AdoptStreamDeckPackMetadata(IconImportBatchEntity batch,
		IReadOnlyList<string> archivePaths,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		if (batch.ExplicitDestination ||
			archivePaths.Count != 1 ||
			!IconImportFiles.IsStreamDeckIconPack(archivePaths[0]) ||
			_iconPackCache.GetIconsByBatchId(batch.Id).Count > 0)
		{
			return;
		}

		(string? name, string? author, string? version) metadata;
		try
		{
			await using var archive = await ZipFile.OpenReadAsync(archivePaths[0], cancellationToken);
			metadata = ParseStreamDeckManifest(archive);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to read .streamDeckIconPack manifest for batch {BatchId}", batch.Id);
			return;
		}

		var pack = new IconPackEntity
		{
			Id = Guid.CreateVersion7(),
			Name = metadata.name ?? Path.GetFileNameWithoutExtension(DisplayNameFor(archivePaths[0])),
			Author = metadata.author,
			Version = metadata.version,
			SourceType = IconPackSourceType.StreamDeckImport,
			CreatedAt = DateTime.UtcNow
		};

		await _iconPackCache.AddOrUpdatePack(pack);
		await mediator.Publish(new IconPackCreatedNotification(pack, 0), cancellationToken);

		batch.PackId = pack.Id;
		_batchTracker.Persist(batch);
	}

	private static (string? Name, string? Author, string? Version) ParseStreamDeckManifest(ZipArchive archive)
	{
		var entry = archive.Entries.FirstOrDefault(e =>
			e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
		if (entry is null)
		{
			return (null, null, null);
		}

		using var stream = entry.Open();
		using var document = JsonDocument.Parse(stream);
		string? name = null, author = null, version = null;
		foreach (var property in document.RootElement.EnumerateObject())
		{
			if (property.Value.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
			{
				name = property.Value.GetString();
			}
			else if (property.Name.Equals("author", StringComparison.OrdinalIgnoreCase))
			{
				author = property.Value.GetString();
			}
			else if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
			{
				version = property.Value.GetString();
			}
		}

		return (name, author, version);
	}

	private static Dictionary<string, string>? ParseStreamDeckIconNames(ZipArchive archive)
	{
		var entry = archive.Entries.FirstOrDefault(e =>
			e.Name.Equals("icons.json", StringComparison.OrdinalIgnoreCase));
		if (entry is null)
		{
			return null;
		}

		try
		{
			using var stream = entry.Open();
			using var document = JsonDocument.Parse(stream);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				return null;
			}

			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var element in document.RootElement.EnumerateArray())
			{
				if (element.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				string? path = null, name = null;
				foreach (var property in element.EnumerateObject())
				{
					if (property.Value.ValueKind != JsonValueKind.String)
					{
						continue;
					}

					if (property.Name.Equals("path", StringComparison.OrdinalIgnoreCase))
					{
						path = property.Value.GetString();
					}
					else if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
					{
						name = property.Value.GetString();
					}
				}

				if (path is not null && name is not null)
				{
					names[Path.GetFileName(path)] = name;
				}
			}

			return names;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string DisplayNameFor(string archivePath)
	{
		var fileName = Path.GetFileName(archivePath);
		var separator = fileName.IndexOf('_');
		return separator > 0 && separator < fileName.Length - 1 ? fileName[(separator + 1)..] : fileName;
	}


	private async Task ReportProgress(Guid batchId,
		IMediator mediator,
		bool force,
		CancellationToken cancellationToken)
	{
		var batch = _batchTracker.Get(batchId);
		if (batch is null || !_batchTracker.ShouldEmitProgress(batchId, force))
		{
			return;
		}

		var (total, processed, failed) = _batchTracker.GetCounters(batchId);
		int? reportedTotal = batch.State == IconImportBatchState.Discovering ? null : total;
		await mediator.Publish(new IconImportProgressNotification(batch, reportedTotal, processed, failed),
			cancellationToken);
	}


	private async Task RecoverInterruptedImports(CancellationToken cancellationToken)
	{
		var liveBatchIds = new HashSet<Guid>();
		foreach (var batch in _batchTracker.LoadPersisted())
		{
			var pack = _iconPackCache.GetPackById(batch.PackId);
			var isTerminal = batch.State is IconImportBatchState.Completed
				or IconImportBatchState.CompletedWithErrors
				or IconImportBatchState.Failed
				or IconImportBatchState.Cancelled;
			if (isTerminal || pack is null)
			{
				_storage.CleanupBatchStaging(batch.Id);
				continue;
			}

			liveBatchIds.Add(batch.Id);
			await RecoverBatch(batch, cancellationToken);
		}

		foreach (var orphanBatchId in _storage.EnumerateStagedBatchIds().Where(id => !liveBatchIds.Contains(id)))
		{
			_storage.CleanupBatchStaging(orphanBatchId);
		}

		await FailOrphanedIcons(liveBatchIds, cancellationToken);
		await _iconPackCache.FlushPendingWrites();
	}

	private async Task RecoverBatch(IconImportBatchEntity batch, CancellationToken cancellationToken)
	{
		var icons = _iconPackCache.GetIconsByBatchId(batch.Id);
		var ready = 0;
		var failed = 0;
		var toProcess = new List<Guid>();
		var toRemove = new List<Guid>();

		foreach (var icon in icons)
		{
			switch (icon.ProcessingState)
			{
				case IconProcessingState.Ready:
					ready++;
					break;
				case IconProcessingState.Failed:
					failed++;
					break;
				default:
					if (HasStagedOriginal(batch.Id, icon.Id))
					{
						if (icon.ProcessingState == IconProcessingState.Processing)
						{
							icon.ProcessingState = IconProcessingState.Pending;
							await _iconPackCache.UpdateIcon(icon);
						}

						toProcess.Add(icon.Id);
					}
					else if (batch.State == IconImportBatchState.Discovering)
					{
						toRemove.Add(icon.Id);
					}
					else
					{
						icon.ProcessingState = IconProcessingState.Failed;
						icon.ProcessingError = "Original file was lost";
						await _iconPackCache.UpdateIcon(icon);
						failed++;
					}

					break;
			}
		}

		if (toRemove.Count > 0)
		{
			await _iconPackCache.RemoveIcons(batch.PackId, toRemove);
		}

		var total = batch.State == IconImportBatchState.Discovering
			? icons.Count - toRemove.Count
			: batch.Total ?? icons.Count;
		_batchTracker.Register(batch);
		_batchTracker.SetCounters(batch.Id, total, ready, failed);

		foreach (var iconId in toProcess)
		{
			_processingChannel.Enqueue(new ProcessIconWorkItem(iconId));
		}

		if (batch.State == IconImportBatchState.Discovering)
		{
			_processingChannel.Enqueue(new ExtractBatchWorkItem(batch.Id));
		}
		else if (toProcess.Count == 0)
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
			await _batchFinalizer.TryFinalize(batch.Id, mediator, cancellationToken);
		}

		_logger.Information(
			"Recovered icon import batch {BatchId}: {Ready} ready, {Failed} failed, {Pending} re-enqueued",
			batch.Id,
			ready,
			failed,
			toProcess.Count);
	}

	private async Task FailOrphanedIcons(HashSet<Guid> liveBatchIds, CancellationToken cancellationToken)
	{
		var orphans = _iconPackCache
			.GetIconsByState(IconProcessingState.Pending, IconProcessingState.Processing)
			.Where(icon => icon.ImportBatchId is null || !liveBatchIds.Contains(icon.ImportBatchId.Value))
			.ToList();

		if (orphans.Count == 0)
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		foreach (var icon in orphans)
		{
			icon.ProcessingState = IconProcessingState.Failed;
			icon.ProcessingError = "The import was interrupted and the original file is no longer available";
			await _iconPackCache.UpdateIcon(icon);
			await mediator.Publish(new IconUpdatedNotification(icon), cancellationToken);
		}

		_logger.Warning("Marked {Count} orphaned icon(s) from interrupted imports as failed", orphans.Count);
	}

	private bool HasStagedOriginal(Guid batchId, Guid iconId)
	{
		var stream = _storage.OpenStagedOriginal(batchId, iconId);
		if (stream is null)
		{
			return false;
		}

		stream.Dispose();
		return true;
	}
}
