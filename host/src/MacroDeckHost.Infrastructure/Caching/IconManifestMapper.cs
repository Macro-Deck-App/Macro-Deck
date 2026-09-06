using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Infrastructure.Caching;

internal static class IconManifestMapper
{
	public static IconPackEntity ToPackEntity(IconPackManifest manifest)
		=> new()
		{
			Id = manifest.Id,
			Name = manifest.Name,
			Description = manifest.Description,
			Author = manifest.Author,
			Version = manifest.Version,
			IsDefault = manifest.IsDefault,
			IsReadOnly = manifest.IsReadOnly,
			SourceType = manifest.SourceType,
			SourceId = manifest.SourceId,
			CreatedAt = manifest.CreatedAt,
			UpdatedAt = manifest.UpdatedAt
		};

	public static IEnumerable<IconEntity> ToIconEntities(IconPackManifest manifest)
		=> manifest.Icons.Select(entry => new IconEntity
		{
			Id = entry.Id,
			PackId = manifest.Id,
			Name = entry.Name,
			Width = entry.Width,
			Height = entry.Height,
			IsAnimated = entry.IsAnimated,
			FrameCount = entry.FrameCount,
			SourceContentHash = ContentHash.Normalize(entry.SourceContentHash),
			MasterContentHash = ContentHash.Normalize(entry.MasterContentHash),
			DeclaredSourceContentHash =
				ContentHash.Normalize(entry.DeclaredSourceContentHash ?? entry.Checksum),
			SourceIconId = entry.SourceIconId,
			OriginalFileName = entry.OriginalFileName,
			OriginalFormat = entry.OriginalFormat,
			ProcessingState = entry.State,
			ProcessingError = entry.ProcessingError,
			AvailableSizes = entry.AvailableSizes.ToList(),
			ImportBatchId = entry.ImportBatchId,
			CreatedAt = entry.CreatedAt,
			UpdatedAt = entry.UpdatedAt
		});

	public static IconPackManifest ToManifest(IconPackEntity pack, IEnumerable<IconEntity> icons)
		=> new()
		{
			Id = pack.Id,
			Name = pack.Name,
			Description = pack.Description,
			Author = pack.Author,
			Version = pack.Version,
			IsDefault = pack.IsDefault,
			IsReadOnly = pack.IsReadOnly,
			SourceType = pack.SourceType,
			SourceId = pack.SourceId,
			CreatedAt = pack.CreatedAt,
			UpdatedAt = pack.UpdatedAt,
			Icons = icons
				.OrderBy(icon => icon.CreatedAt)
				.Select(icon => new IconManifestEntry
				{
					Id = icon.Id,
					Name = icon.Name,
					Width = icon.Width,
					Height = icon.Height,
					IsAnimated = icon.IsAnimated,
					FrameCount = icon.FrameCount,
					SourceContentHash = icon.SourceContentHash,
					MasterContentHash = icon.MasterContentHash,
					DeclaredSourceContentHash = icon.DeclaredSourceContentHash,
					Checksum = StripPrefix(icon.SourceContentHash ?? icon.DeclaredSourceContentHash),
					SourceIconId = icon.SourceIconId,
					OriginalFileName = icon.OriginalFileName,
					OriginalFormat = icon.OriginalFormat,
					State = icon.ProcessingState,
					ProcessingError = icon.ProcessingError,
					AvailableSizes = icon.AvailableSizes.ToList(),
					ImportBatchId = icon.ImportBatchId,
					CreatedAt = icon.CreatedAt,
					UpdatedAt = icon.UpdatedAt
				})
				.ToList()
		};

	private static string? StripPrefix(string? hash)
		=> hash is null || !hash.StartsWith(ContentHash.Sha256Prefix, StringComparison.Ordinal)
			? hash
			: hash[ContentHash.Sha256Prefix.Length..];
}
