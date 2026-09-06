using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Packaging;
using MacroDeckHost.Domain.Common;
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

	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly ILogger _logger;

	public IconPackExportService(IIconPackCache iconPackCache, IIconStorage storage, ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
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
		foreach (var entry in manifest.Icons)
		{
			entry.ImportBatchId = null;
		}

		await using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
		var files = new List<PackageFileDigest>();

		foreach (var icon in icons)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var variants = icon.AvailableSizes
				.Select(size => size.ToString(CultureInfo.InvariantCulture))
				.Append(IconVariants.Master);
			foreach (var variant in variants)
			{
				var content = _storage.OpenVariant(packId, icon.Id, variant);
				if (content is null)
				{
					_logger.Warning("Missing variant {Variant} for icon {IconId} in pack {PackId}; skipping",
						variant,
						icon.Id,
						packId);
					continue;
				}

				await using (content)
				{
					var path = $"icons/{icon.Id}/{variant}.webp";
					var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
					await using var entryStream = await entry.OpenAsync(cancellationToken);
					var digest = await CopyAndHash(content, entryStream, cancellationToken);
					files.Add(new PackageFileDigest { Path = path, Sha256 = digest.Sha256, Size = digest.Size });
				}
			}
		}

		files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
		manifest.Files = files;

		var manifestEntry = archive.CreateEntry("pack.json");
		await using (var manifestStream = await manifestEntry.OpenAsync(cancellationToken))
		{
			await JsonSerializer.SerializeAsync(manifestStream,
				manifest,
				PersistenceJsonOptions.Default,
				cancellationToken);
		}

		return Result.Ok<IconPackError>();
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
