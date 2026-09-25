using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Persistence;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class IconPackRestoreService : IIconPackRestoreService
{
	private const int MaxArchiveEntries = 10_000;
	private const string RestoreStagingDirectoryName = "_restore";

	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IIconPackStore _packStore;
	private readonly IMacroDeckPaths _paths;
	private readonly IMediator _mediator;
	private readonly IIconPackOwnerRegistry _ownerRegistry;
	private readonly ILogger _logger;

	public IconPackRestoreService(
		IIconPackCache iconPackCache,
		IIconStorage storage,
		IIconPackStore packStore,
		IMacroDeckPaths paths,
		IMediator mediator,
		IIconPackOwnerRegistry ownerRegistry,
		ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_packStore = packStore;
		_paths = paths;
		_mediator = mediator;
		_ownerRegistry = ownerRegistry;
		_logger = logger;
	}

	public async Task<Result<IconPackEntity, IconError>> RestoreAsNewPack(string fileName,
		Stream content,
		CancellationToken cancellationToken,
		IconPackSourceStamp? stamp = null)
	{
		var tempPath = await StageToTempFile(content, cancellationToken);
		try
		{
			await using var archive = await ZipFile.OpenReadAsync(tempPath, cancellationToken);
			var manifestResult = ReadManifest(archive, fileName);
			if (!manifestResult.Success)
			{
				return Result.Fail<IconPackEntity, IconError>(manifestResult.Error!.Value,
					manifestResult.ErrorMessage);
			}

			var manifest = manifestResult.Data!;
			var pack = new IconPackEntity
			{
				Id = Guid.CreateVersion7(),
				Name = string.IsNullOrWhiteSpace(manifest.Name)
					? Path.GetFileNameWithoutExtension(fileName)
					: manifest.Name.Trim(),
				Description = manifest.Description,
				Author = manifest.Author,
				Version = manifest.Version,
				AiAssets = IconPackAiDeclarations.FromManifest(manifest.Ai),
				SourceType = stamp?.SourceType ?? IconPackSourceType.MacroDeckImport,
				SourceId = stamp?.SourceId,
				SourceRevision = stamp?.SourceRevision,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			};

			List<IconEntity> icons;
			try
			{
				icons = await CopyIcons(archive, manifest, pack.Id, importBatchId: null, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Error(ex, "Failed to restore icon pack from {FileName}", fileName);
				_packStore.Delete(pack.Id);
				return Result.Fail<IconPackEntity, IconError>(IconError.StorageFailure);
			}

			await _iconPackCache.AddOrUpdatePack(pack);
			await _iconPackCache.AddIcons(pack.Id, icons);
			await _mediator.Publish(new IconPackCreatedNotification(pack, icons.Count), cancellationToken);
			return Result.Ok<IconPackEntity, IconError>(pack);
		}
		catch (InvalidDataException)
		{
			return Result.Fail<IconPackEntity, IconError>(IconError.InvalidArchive,
				"The file is not a valid .macroDeckIconPack archive");
		}
		finally
		{
			TryDeleteTempFile(tempPath);
		}
	}

	public async Task<Result<IReadOnlyList<IconEntity>, IconError>> MergeIntoPack(Guid packId,
		Guid importBatchId,
		string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var pack = _iconPackCache.GetPackById(packId);
		if (pack is null)
		{
			return Result.Fail<IReadOnlyList<IconEntity>, IconError>(IconError.PackNotFound);
		}

		if (_ownerRegistry.IsReadOnly(pack))
		{
			return Result.Fail<IReadOnlyList<IconEntity>, IconError>(IconError.PackReadOnly);
		}

		var tempPath = await StageToTempFile(content, cancellationToken);
		try
		{
			await using var archive = await ZipFile.OpenReadAsync(tempPath, cancellationToken);
			var manifestResult = ReadManifest(archive, fileName);
			if (!manifestResult.Success)
			{
				return Result.Fail<IReadOnlyList<IconEntity>, IconError>(manifestResult.Error!.Value,
					manifestResult.ErrorMessage);
			}

			List<IconEntity> icons;
			try
			{
				icons = await CopyIcons(archive, manifestResult.Data!, packId, importBatchId, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Error(ex, "Failed to merge icon pack {FileName} into pack {PackId}", fileName, packId);
				return Result.Fail<IReadOnlyList<IconEntity>, IconError>(IconError.StorageFailure);
			}

			await _iconPackCache.AddIcons(packId, icons);
			await _iconPackCache.ForgetSourceRevision(packId);
			var aiAssets = IconPackAiDeclarations.Merge(pack.AiAssets, IconPackAiDeclarations.FromManifest(manifestResult.Data!.Ai));
			if (icons.Count > 0 && aiAssets != pack.AiAssets)
			{
				pack.AiAssets = aiAssets;
				pack.UpdatedAt = DateTime.UtcNow;
				await _iconPackCache.AddOrUpdatePack(pack);
				await _mediator.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(packId)),
					cancellationToken);
			}

			return Result.Ok<IReadOnlyList<IconEntity>, IconError>(icons);
		}
		catch (InvalidDataException)
		{
			return Result.Fail<IReadOnlyList<IconEntity>, IconError>(IconError.InvalidArchive,
				"The file is not a valid .macroDeckIconPack archive");
		}
		finally
		{
			TryDeleteTempFile(tempPath);
		}
	}

	public async Task<Result<IconPackEntity, IconError>> UpgradePack(Guid packId,
		string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		var pack = _iconPackCache.GetPackById(packId);
		if (pack is null)
		{
			return Result.Fail<IconPackEntity, IconError>(IconError.PackNotFound);
		}

		if (pack.IsReadOnly)
		{
			return Result.Fail<IconPackEntity, IconError>(IconError.PackReadOnly);
		}

		var tempPath = await StageToTempFile(content, cancellationToken);
		try
		{
			await using var archive = await ZipFile.OpenReadAsync(tempPath, cancellationToken);
			var manifestResult = ReadManifest(archive, fileName);
			if (!manifestResult.Success)
			{
				return Result.Fail<IconPackEntity, IconError>(manifestResult.Error!.Value,
					manifestResult.ErrorMessage);
			}

			var manifest = manifestResult.Data!;
			var installed = _iconPackCache.GetIconsByPackId(packId);
			var variantsByIconId = IndexVariantEntries(archive);
			var added = new List<IconEntity>();

			foreach (var entry in manifest.Icons)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!variantsByIconId.TryGetValue(entry.Id, out var variants) ||
					!variants.ContainsKey(IconVariants.Master))
				{
					continue;
				}

				var existing = Correlate(installed, entry);
				var icon = existing ??
					new IconEntity
					{
						Id = Guid.CreateVersion7(),
						PackId = packId,
						Name = entry.Name,
						CreatedAt = entry.CreatedAt == default ? DateTime.UtcNow : entry.CreatedAt
					};

				var master = await WriteVariants(variants, packId, icon.Id, entry, cancellationToken);
				if (master is null)
				{
					if (existing is null)
					{
						_storage.DeleteIconFiles(packId, icon.Id);
					}

					continue;
				}

				icon.Name = entry.Name;
				icon.Width = entry.Width;
				icon.Height = entry.Height;
				icon.IsAnimated = entry.IsAnimated;
				icon.FrameCount = entry.FrameCount;
				icon.SourceIconId = entry.Id;
				icon.DeclaredSourceContentHash =
					ContentHash.Normalize(entry.SourceContentHash ?? entry.Checksum);
				icon.OriginalFileName = entry.OriginalFileName;
				icon.OriginalFormat = entry.OriginalFormat;
				icon.ProcessingState = IconProcessingState.Ready;
				icon.MasterContentHash = master;
				icon.AvailableSizes = variants.Keys
					.Where(variant => variant != IconVariants.Master)
					.Select(int.Parse)
					.Order()
					.ToList();
				icon.UpdatedAt = DateTime.UtcNow;

				if (existing is null)
				{
					added.Add(icon);
				}
				else
				{
					await _iconPackCache.UpdateIcon(icon);
				}
			}

			if (added.Count > 0)
			{
				await _iconPackCache.AddIcons(packId, added);
			}

			// Icons the new version dropped are deliberately kept: a button may still reference one, and
			// silently emptying a button on an update the user did not author is the worse failure.
			pack.Name = string.IsNullOrWhiteSpace(manifest.Name) ? pack.Name : manifest.Name.Trim();
			pack.Description = manifest.Description ?? pack.Description;
			pack.Author = manifest.Author ?? pack.Author;
			pack.Version = manifest.Version;
			pack.AiAssets = manifest.Ai is null ? pack.AiAssets : IconPackAiDeclarations.FromManifest(manifest.Ai);
			pack.UpdatedAt = DateTime.UtcNow;
			await _iconPackCache.AddOrUpdatePack(pack);
			await _mediator.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(packId)),
				cancellationToken);

			return Result.Ok<IconPackEntity, IconError>(pack);
		}
		catch (InvalidDataException)
		{
			return Result.Fail<IconPackEntity, IconError>(IconError.InvalidArchive,
				"The file is not a valid .macroDeckIconPack archive");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Error(ex, "Failed to upgrade icon pack {PackId} from {FileName}", packId, fileName);
			return Result.Fail<IconPackEntity, IconError>(IconError.StorageFailure);
		}
		finally
		{
			TryDeleteTempFile(tempPath);
		}
	}

	public async Task<Result<IconPackReplaceOutcome, IconError>> ReplaceFromSource(Guid packId,
		string fileName,
		Stream content,
		IconPackSourceStamp stamp,
		IReadOnlySet<Guid> iconsInUse,
		CancellationToken cancellationToken)
	{
		var pack = _iconPackCache.GetPackById(packId);
		if (pack is null)
		{
			return Result.Fail<IconPackReplaceOutcome, IconError>(IconError.PackNotFound);
		}

		var tempPath = await StageToTempFile(content, cancellationToken);
		try
		{
			await using var archive = await ZipFile.OpenReadAsync(tempPath, cancellationToken);
			var manifestResult = ReadManifest(archive, fileName);
			if (!manifestResult.Success)
			{
				return Result.Fail<IconPackReplaceOutcome, IconError>(manifestResult.Error!.Value,
					manifestResult.ErrorMessage);
			}

			var manifest = manifestResult.Data!;
			var variantsByIconId = IndexVariantEntries(archive);
			var unmatched = _iconPackCache.GetIconsByPackId(packId).OrderBy(icon => icon.CreatedAt).ToList();
			var added = new List<IconEntity>();
			var updated = new List<IconEntity>();
			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var entry in manifest.Icons)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!seenNames.Add(entry.Name) ||
					!variantsByIconId.TryGetValue(entry.Id, out var variants) ||
					!variants.TryGetValue(IconVariants.Master, out var masterEntry))
				{
					continue;
				}

				// Names are the identity; the archive id only finds an icon the user renamed since the same
				// archive was applied, since every fresh export of a local pack mints new ids.
				var existing = unmatched.FirstOrDefault(icon =>
						string.Equals(icon.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) ??
					unmatched.FirstOrDefault(icon => icon.SourceIconId == entry.Id &&
						!manifest.Icons.Any(other => string.Equals(other.Name, icon.Name, StringComparison.OrdinalIgnoreCase)));
				if (existing is not null)
				{
					unmatched.Remove(existing);
				}

				var incomingMaster = await HashEntry(masterEntry, cancellationToken);
				var declared = ContentHash.Normalize(entry.MasterContentHash);
				if (declared is not null && declared != incomingMaster)
				{
					_logger.Warning("Icon {Name} in pack archive {FileName} fails its declared master hash; skipping",
						entry.Name,
						fileName);
					continue;
				}

				if (existing is not null &&
					existing.ProcessingState == IconProcessingState.Ready &&
					existing.MasterContentHash == incomingMaster)
				{
					if (!string.Equals(existing.Name, entry.Name, StringComparison.Ordinal))
					{
						existing.Name = entry.Name;
						existing.UpdatedAt = DateTime.UtcNow;
						await _iconPackCache.UpdateIcon(existing);
						updated.Add(existing);
					}

					continue;
				}

				var icon = existing ??
					new IconEntity
					{
						Id = Guid.CreateVersion7(),
						PackId = packId,
						Name = entry.Name,
						CreatedAt = DateTime.UtcNow
					};

				var master = await WriteVariants(variants, packId, icon.Id, entry, cancellationToken);
				if (master is null)
				{
					if (existing is null)
					{
						_storage.DeleteIconFiles(packId, icon.Id);
					}

					continue;
				}

				ApplyEntry(icon, entry, variants, master);
				if (existing is null)
				{
					added.Add(icon);
				}
				else
				{
					await _iconPackCache.UpdateIcon(icon);
					updated.Add(icon);
				}
			}

			if (added.Count > 0)
			{
				await _iconPackCache.AddIcons(packId, added);
			}

			var dropped = unmatched.Where(icon => !iconsInUse.Contains(icon.Id)).ToList();
			if (dropped.Count > 0)
			{
				await _iconPackCache.RemoveIcons(packId, dropped.Select(icon => icon.Id).ToList());
				foreach (var icon in dropped)
				{
					_storage.DeleteIconFiles(packId, icon.Id);
				}
			}

			pack.Name = string.IsNullOrWhiteSpace(manifest.Name) ? pack.Name : manifest.Name.Trim();
			pack.Description = manifest.Description;
			pack.Author = manifest.Author;
			pack.Version = manifest.Version;
			pack.AiAssets = IconPackAiDeclarations.FromManifest(manifest.Ai);
			pack.SourceType = stamp.SourceType;
			pack.SourceId = stamp.SourceId;
			pack.SourceRevision = stamp.SourceRevision;
			pack.UpdatedAt = DateTime.UtcNow;
			await _iconPackCache.AddOrUpdatePack(pack);

			foreach (var icon in updated)
			{
				await _mediator.Publish(new IconUpdatedNotification(icon), cancellationToken);
			}

			foreach (var icon in dropped)
			{
				await _mediator.Publish(new IconDeletedNotification(icon.Id, packId), cancellationToken);
			}

			if (added.Count > 0)
			{
				await _mediator.Publish(new IconsAddedNotification(null, packId, added), cancellationToken);
			}

			await _mediator.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(packId)),
				cancellationToken);

			return Result.Ok<IconPackReplaceOutcome, IconError>(new IconPackReplaceOutcome(pack,
				IconsChanged: added.Count > 0 || updated.Count > 0 || dropped.Count > 0,
				KeptInUse: unmatched.Count - dropped.Count));
		}
		catch (InvalidDataException)
		{
			return Result.Fail<IconPackReplaceOutcome, IconError>(IconError.InvalidArchive,
				"The file is not a valid .macroDeckIconPack archive");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Error(ex, "Failed to replace icon pack {PackId} from {FileName}", packId, fileName);
			return Result.Fail<IconPackReplaceOutcome, IconError>(IconError.StorageFailure);
		}
		finally
		{
			TryDeleteTempFile(tempPath);
		}
	}

	private static async Task<string> HashEntry(ZipArchiveEntry entry, CancellationToken cancellationToken)
	{
		await using var stream = await entry.OpenAsync(cancellationToken);
		using var hash = ContentHash.CreateIncremental();
		var buffer = new byte[81_920];
		int read;
		while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
		{
			hash.Append(buffer.AsSpan(0, read));
		}

		return hash.Finish();
	}

	private static void ApplyEntry(IconEntity icon,
		IconManifestEntry entry,
		Dictionary<string, ZipArchiveEntry> variants,
		string master)
	{
		icon.Name = entry.Name;
		icon.Width = entry.Width;
		icon.Height = entry.Height;
		icon.IsAnimated = entry.IsAnimated;
		icon.FrameCount = entry.FrameCount;
		icon.SourceIconId = entry.Id;
		icon.DeclaredSourceContentHash = ContentHash.Normalize(entry.SourceContentHash ?? entry.Checksum);
		icon.OriginalFileName = entry.OriginalFileName;
		icon.OriginalFormat = entry.OriginalFormat;
		icon.ProcessingState = IconProcessingState.Ready;
		icon.ProcessingError = null;
		icon.MasterContentHash = master;
		icon.AvailableSizes = variants.Keys
			.Where(variant => variant != IconVariants.Master)
			.Select(int.Parse)
			.Order()
			.ToList();
		icon.UpdatedAt = DateTime.UtcNow;
	}

	// Packs installed before the source id was recorded still correlate, by the source hash the archive
	// declared and then by name, so the first upgrade of an older installation keeps its icon ids too.
	private static IconEntity? Correlate(List<IconEntity> installed, IconManifestEntry entry)
	{
		var bySourceId = installed.FirstOrDefault(icon => icon.SourceIconId == entry.Id);
		if (bySourceId is not null)
		{
			return bySourceId;
		}

		var declared = ContentHash.Normalize(entry.SourceContentHash ?? entry.Checksum);
		var byHash = declared is null
			? null
			: installed.FirstOrDefault(icon => icon.SourceIconId is null && icon.DeclaredSourceContentHash == declared);

		return byHash ??
			installed.FirstOrDefault(icon =>
				icon.SourceIconId is null && string.Equals(icon.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
	}

	private async Task<string?> WriteVariants(Dictionary<string, ZipArchiveEntry> variants,
		Guid packId,
		Guid iconId,
		IconManifestEntry entry,
		CancellationToken cancellationToken)
	{
		string? master = null;
		foreach (var (variant, zipEntry) in variants)
		{
			await using var entryStream = await zipEntry.OpenAsync(cancellationToken);
			var written = await _storage.WriteVariant(packId, iconId, variant, entryStream, cancellationToken);
			if (variant != IconVariants.Master)
			{
				continue;
			}

			var declared = ContentHash.Normalize(entry.MasterContentHash);
			if (declared is not null && declared != written)
			{
				_logger.Warning("Icon {IconId} fails its declared master hash during upgrade", entry.Id);
				return null;
			}

			master = written;
		}

		return master;
	}

	private async Task<string> StageToTempFile(Stream content, CancellationToken cancellationToken)
	{
		var directory = Path.Combine(_paths.IconStagingDirectory, RestoreStagingDirectoryName);
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, $"{Guid.CreateVersion7():N}.tmp");
		await using var file = File.Create(path);
		await content.CopyToAsync(file, cancellationToken);

		return path;
	}

	private Result<IconPackManifest, IconError> ReadManifest(ZipArchive archive, string fileName)
	{
		if (archive.Entries.Count > MaxArchiveEntries)
		{
			return Result.Fail<IconPackManifest, IconError>(IconError.InvalidArchive,
				$"The archive contains more than {MaxArchiveEntries} entries");
		}

		var manifestEntry = archive.GetEntry("pack.json");
		if (manifestEntry is null)
		{
			return Result.Fail<IconPackManifest, IconError>(IconError.InvalidArchive,
				"The archive contains no pack.json manifest");
		}

		try
		{
			using var stream = manifestEntry.Open();
			var manifest = JsonSerializer.Deserialize<IconPackManifest>(stream, PersistenceJsonOptions.Default);
			if (manifest is null)
			{
				return Result.Fail<IconPackManifest, IconError>(IconError.InvalidArchive,
					"The pack.json manifest is empty");
			}

			return Result.Ok<IconPackManifest, IconError>(manifest);
		}
		catch (JsonException ex)
		{
			_logger.Warning(ex, "Unparseable pack.json in {FileName}", fileName);
			return Result.Fail<IconPackManifest, IconError>(IconError.InvalidArchive,
				"The pack.json manifest is not parseable");
		}
	}

	private async Task<List<IconEntity>> CopyIcons(ZipArchive archive,
		IconPackManifest manifest,
		Guid targetPackId,
		Guid? importBatchId,
		CancellationToken cancellationToken)
	{
		var variantsByIconId = IndexVariantEntries(archive);
		var icons = new List<IconEntity>();
		try
		{
			foreach (var entry in manifest.Icons)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!variantsByIconId.TryGetValue(entry.Id, out var variants) ||
					!variants.ContainsKey(IconVariants.Master))
				{
					_logger.Warning("Icon {IconId} in pack archive has no master file; skipping", entry.Id);
					continue;
				}

				var icon = new IconEntity
				{
					Id = Guid.CreateVersion7(),
					PackId = targetPackId,
					Name = entry.Name,
					Width = entry.Width,
					Height = entry.Height,
					IsAnimated = entry.IsAnimated,
					FrameCount = entry.FrameCount,
					SourceIconId = entry.Id,
					DeclaredSourceContentHash =
						ContentHash.Normalize(entry.SourceContentHash ?? entry.Checksum),
					OriginalFileName = entry.OriginalFileName,
					OriginalFormat = entry.OriginalFormat,
					ProcessingState = IconProcessingState.Ready,
					AvailableSizes = variants.Keys
						.Where(variant => variant != IconVariants.Master)
						.Select(int.Parse)
						.Order()
						.ToList(),
					ImportBatchId = importBatchId,
					CreatedAt = entry.CreatedAt == default ? DateTime.UtcNow : entry.CreatedAt,
					UpdatedAt = DateTime.UtcNow
				};

				var corrupted = false;
				foreach (var (variant, zipEntry) in variants)
				{
					await using var entryStream = await zipEntry.OpenAsync(cancellationToken);
					var written = await _storage.WriteVariant(targetPackId,
						icon.Id,
						variant,
						entryStream,
						cancellationToken);

					if (variant != IconVariants.Master)
					{
						continue;
					}

					// Verified against what landed on disk, not against what the manifest claims the source
					// was: a declared hash that does not match the bytes beside it means the archive is
					// corrupt or edited, and a wrong image is worse than a missing one. Only the master is
					// checked - it is the rendition every size falls back to, and the one identity that
					// travels with the icon.
					var declared = ContentHash.Normalize(entry.MasterContentHash);
					if (declared is not null && declared != written)
					{
						_logger.Warning("Icon {IconId} in pack archive fails its declared master hash; skipping",
							entry.Id);
						corrupted = true;
						break;
					}

					icon.MasterContentHash = written;
				}

				if (corrupted)
				{
					_storage.DeleteIconFiles(targetPackId, icon.Id);
					continue;
				}

				icons.Add(icon);
			}
		}
		catch
		{
			foreach (var icon in icons)
			{
				_storage.DeleteIconFiles(targetPackId, icon.Id);
			}

			throw;
		}

		return icons;
	}

	private static Dictionary<Guid, Dictionary<string, ZipArchiveEntry>> IndexVariantEntries(ZipArchive archive)
	{
		var result = new Dictionary<Guid, Dictionary<string, ZipArchiveEntry>>();
		foreach (var entry in archive.Entries)
		{
			var segments = entry.FullName.Replace('\\', '/').Split('/');
			if (segments.Length != 3 ||
				!segments[0].Equals("icons", StringComparison.OrdinalIgnoreCase) ||
				!Guid.TryParse(segments[1], out var iconId))
			{
				continue;
			}

			var fileName = segments[2];
			if (!fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var variant = Path.GetFileNameWithoutExtension(fileName);
			var isMaster = variant.Equals(IconVariants.Master, StringComparison.OrdinalIgnoreCase);
			if (!isMaster && !int.TryParse(variant, NumberStyles.None, CultureInfo.InvariantCulture, out _))
			{
				continue;
			}

			var variants = result.TryGetValue(iconId, out var existing) ? existing : result[iconId] = new();
			variants[isMaster ? IconVariants.Master : variant] = entry;
		}

		return result;
	}

	private void TryDeleteTempFile(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to delete restore temp file {Path}", path);
		}
	}
}
