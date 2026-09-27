using System.IO.Compression;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Packaging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Icons;

public sealed class IconPackExportService : IIconPackExportService
{
	public const string FileExtension = ".macroDeckIconPack";

	// Fixed set instead of Path.GetInvalidFileNameChars(): the download lands on arbitrary client
	// platforms, so the name must be safe everywhere regardless of the host OS.
	private static readonly char[] _invalidFileNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

	private const string ExportStagingDirectoryName = "_export";
	private static readonly TimeSpan _staleStagingAge = TimeSpan.FromDays(1);

	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IMacroDeckPaths _paths;
	private readonly ILogger _logger;

	public IconPackExportService(IIconPackCache iconPackCache, IIconStorage storage, IMacroDeckPaths paths, ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_paths = paths;
		_logger = logger;
	}

	public Result<string, IconPackError> GetExportFileName(Guid packId)
	{
		var pack = _iconPackCache.GetPackById(packId);
		if (pack is null)
		{
			return Result.Fail<string, IconPackError>(IconPackError.NotFound);
		}

		var name = string.Concat(pack.Name.Where(c => !_invalidFileNameChars.Contains(c) && !char.IsControl(c)))
			.Trim();
		if (name.Length == 0)
		{
			name = "icon-pack";
		}

		return Result.Ok<string, IconPackError>(name + FileExtension);
	}

	public async Task<Result<IconPackError>> Export(Guid packId,
		Stream destination,
		CancellationToken cancellationToken)
	{
		var pack = _iconPackCache.GetPackById(packId);
		if (pack is null)
		{
			return Result.Fail(IconPackError.NotFound);
		}

		await _iconPackCache.FlushPendingWrites();
		var icons = _iconPackCache.GetIconsByPackId(packId)
			.Where(icon => icon.ProcessingState == IconProcessingState.Ready)
			.ToList();

		var manifest = IconManifestMapper.ToManifest(pack, icons);
		manifest.IsDefault = false;
		manifest.IsReadOnly = false;
		manifest.SourceId = null;
		manifest.SourceRevision = null;
		// Hosts older than the Plugin source type cannot read it back, so an export never carries it.
		if (manifest.SourceType == IconPackSourceType.Plugin)
		{
			manifest.SourceType = IconPackSourceType.User;
		}
		foreach (var entry in manifest.Icons)
		{
			entry.ImportBatchId = null;
			entry.AvailableSizes = [];
		}

		// Counted before anything is written, and the archive is staged, so a refused pack sends nothing.
		if (icons.Count + 1 > IconPackArchiveLimits.MaxUnsignedEntries)
		{
			return Result.Fail(IconPackError.TooLarge);
		}

		var stagingDirectory = Path.Combine(_paths.IconStagingDirectory, ExportStagingDirectoryName);
		Directory.CreateDirectory(stagingDirectory);
		DeleteAbandonedStagingFiles(stagingDirectory);
		await using var staging = new FileStream(Path.Combine(stagingDirectory, $"{Guid.CreateVersion7():N}.tmp"),
			FileMode.CreateNew,
			FileAccess.ReadWrite,
			FileShare.None,
			81_920,
			FileOptions.DeleteOnClose | FileOptions.Asynchronous);
		if (!await WriteArchive(packId, manifest, icons, staging, cancellationToken))
		{
			return Result.Fail(IconPackError.TooLarge);
		}

		staging.Position = 0;
		await staging.CopyToAsync(destination, cancellationToken);

		return Result.Ok<IconPackError>();
	}

	// DeleteOnClose does not survive a crash on Unix, so a file an export left behind is swept later.
	private void DeleteAbandonedStagingFiles(string stagingDirectory)
	{
		var cutoff = DateTime.UtcNow - _staleStagingAge;
		foreach (var file in new DirectoryInfo(stagingDirectory).EnumerateFiles("*.tmp"))
		{
			if (file.LastWriteTimeUtc >= cutoff)
			{
				continue;
			}

			try
			{
				file.Delete();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.Debug(ex, "Could not delete abandoned export staging file {Path}", file.FullName);
			}
		}
	}

	private async Task<bool> WriteArchive(Guid packId,
		IconPackManifest manifest,
		IReadOnlyList<IconEntity> icons,
		Stream destination,
		CancellationToken cancellationToken)
	{
		await using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
		var files = new List<PackageFileDigest>();

		foreach (var icon in icons)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var content = _storage.OpenVariant(packId, icon.Id, IconVariants.Master);
			if (content is null)
			{
				_logger.Warning("Missing master for icon {IconId} in pack {PackId}; skipping", icon.Id, packId);
				continue;
			}

			await using (content)
			{
				var path = $"icons/{icon.Id}/{IconVariants.Master}.webp";
				var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
				await using var entryStream = await entry.OpenAsync(cancellationToken);
				var digest = await CopyAndHash(content, entryStream, cancellationToken);
				files.Add(new PackageFileDigest { Path = path, Sha256 = digest.Sha256, Size = digest.Size });
			}
		}

		files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
		manifest.Files = files;

		var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PersistenceJsonOptions.Default);
		if (manifestBytes.Length > IconPackArchiveLimits.MaxUnsignedManifestBytes)
		{
			return false;
		}

		var manifestEntry = archive.CreateEntry("pack.json");
		await using (var manifestStream = await manifestEntry.OpenAsync(cancellationToken))
		{
			await manifestStream.WriteAsync(manifestBytes, cancellationToken);
		}

		return true;
	}

	private static async Task<(string Sha256, long Size)> CopyAndHash(Stream source,
		Stream destination,
		CancellationToken cancellationToken)
	{
		using var hash = ContentHash.CreateIncremental();
		var buffer = new byte[81_920];
		long size = 0;
		int read;
		while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
		{
			hash.Append(buffer.AsSpan(0, read));
			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			size += read;
		}

		return (hash.Finish(), size);
	}
}
