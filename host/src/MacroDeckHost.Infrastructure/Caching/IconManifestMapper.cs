using MacroDeckHost.Application.Icons;
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
			SourceRevision = manifest.SourceRevision,
			AiAssets = IconPackAiDeclarations.FromManifest(manifest.Ai),
			CreatedAt = manifest.CreatedAt,
			UpdatedAt = manifest.UpdatedAt
		};

	public static IEnumerable<IconEntity> ToIconEntities(IconPackManifest manifest)
		=> manifest.Icons.SelectMany(entry => Flatten(manifest.Id, entry, parent: null));

	public static IconPackManifest ToManifest(IconPackEntity pack, IEnumerable<IconEntity> icons)
	{
		var all = icons.ToList();
		var appearancesByParent = all
			.Where(icon => icon.AppearanceOfId is not null)
			.GroupBy(icon => icon.AppearanceOfId!.Value)
			.ToDictionary(group => group.Key,
				group => group
					.OrderBy(asset => asset.AppearanceTraits is { } traits ? IconAppearanceTraits.ToKey(traits) : string.Empty,
						StringComparer.Ordinal)
					.ToList());

		return new IconPackManifest
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
			SourceRevision = pack.SourceRevision,
			Ai = IconPackAiDeclarations.ToManifest(pack.AiAssets),
			CreatedAt = pack.CreatedAt,
			UpdatedAt = pack.UpdatedAt,
			Icons = all
				.Where(icon => icon.AppearanceOfId is null)
				.OrderBy(icon => icon.CreatedAt)
				.Select(icon => ToEntry(icon,
					appearancesByParent.TryGetValue(icon.Id, out var appearances)
						? appearances.Select(asset => ToEntry(asset, appearances: null, icon.Name)).ToList()
						: null))
				.ToList()
		};
	}

	private static IEnumerable<IconEntity> Flatten(Guid packId, IconManifestEntry entry, IconManifestEntry? parent)
	{
		yield return ToEntity(packId, entry, parent);

		foreach (var appearance in entry.Appearances ?? [])
		{
			foreach (var nested in Flatten(packId, appearance, entry))
			{
				yield return nested;
			}
		}
	}

	private static IconEntity ToEntity(Guid packId, IconManifestEntry entry, IconManifestEntry? parent)
		=> new()
		{
			Id = entry.Id,
			PackId = packId,
			Name = parent?.Name ?? entry.Name,
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
			AppearanceOfId = parent?.Id,
			AppearanceTraits = parent is null
				? null
				: new Dictionary<string, string>(entry.Traits ?? [], StringComparer.Ordinal),
			CreatedAt = entry.CreatedAt,
			UpdatedAt = entry.UpdatedAt
		};

	private static IconManifestEntry ToEntry(IconEntity icon, List<IconManifestEntry>? appearances, string? name = null)
		=> new()
		{
			Id = icon.Id,
			Name = name ?? icon.Name,
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
			UpdatedAt = icon.UpdatedAt,
			Traits = icon.AppearanceTraits is { } traits
				? new Dictionary<string, string>(traits, StringComparer.Ordinal)
				: null,
			Appearances = appearances is { Count: > 0 } ? appearances : null
		};

	private static string? StripPrefix(string? hash)
		=> hash is null || !hash.StartsWith(ContentHash.Sha256Prefix, StringComparison.Ordinal)
			? hash
			: hash[ContentHash.Sha256Prefix.Length..];
}
