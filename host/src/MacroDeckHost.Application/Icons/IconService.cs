using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Icons;

public class IconService : IIconService
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IIconImageFallbackStore _fallbackStore;
	private readonly IconImportCoalescer _coalescer;
	private readonly IMediator _mediator;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public IconService(IIconPackCache iconPackCache,
		IIconStorage storage,
		IIconImageFallbackStore fallbackStore,
		IconImportCoalescer coalescer,
		IMediator mediator,
		IIconPackOwnerRegistry ownerRegistry)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_fallbackStore = fallbackStore;
		_coalescer = coalescer;
		_mediator = mediator;
		_ownerRegistry = ownerRegistry;
	}

	public async Task<Result<IconEntity, IconError>> Rename(Guid iconId, string name)
	{
		var icon = _iconPackCache.GetIconById(iconId);
		if (icon is null)
		{
			return Result.Fail<IconEntity, IconError>(IconError.NotFound);
		}

		if (IsPackReadOnly(icon.PackId))
		{
			return Result.Fail<IconEntity, IconError>(IconError.PackReadOnly,
				"Icons in read-only packs cannot be renamed");
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<IconEntity, IconError>(IconError.ValidationError, "A name is required");
		}

		icon.Name = name.Trim();
		await _iconPackCache.UpdateIcon(icon);
		await _mediator.Publish(new IconUpdatedNotification(icon));
		return Result.Ok<IconEntity, IconError>(icon);
	}

	public async Task<Result<IconError>> Delete(Guid iconId)
	{
		var icon = _iconPackCache.GetIconById(iconId);
		if (icon is null)
		{
			return Result.Fail(IconError.NotFound);
		}

		if (IsPackReadOnly(icon.PackId))
		{
			return Result.Fail(IconError.PackReadOnly, "Icons in read-only packs cannot be deleted");
		}

		await _iconPackCache.RemoveIcon(iconId);
		_coalescer.ReleaseAll(iconId);
		_storage.DeleteIconFiles(icon.PackId, iconId);
		await _mediator.Publish(new IconDeletedNotification(iconId, icon.PackId));
		return Result.Ok<IconError>();
	}

	public async Task<Result<int, IconError>> DeleteMany(IReadOnlyList<Guid> iconIds)
	{
		var icons = iconIds
			.Distinct()
			.Select(_iconPackCache.GetIconById)
			.OfType<IconEntity>()
			.ToList();

		foreach (var packId in icons.Select(i => i.PackId).Distinct())
		{
			if (IsPackReadOnly(packId))
			{
				return Result.Fail<int, IconError>(IconError.PackReadOnly,
					"Icons in read-only packs cannot be deleted");
			}
		}

		foreach (var packGroup in icons.GroupBy(i => i.PackId))
		{
			await _iconPackCache.RemoveIcons(packGroup.Key, packGroup.Select(i => i.Id).ToList());
			foreach (var icon in packGroup)
			{
				_coalescer.ReleaseAll(icon.Id);
				_storage.DeleteIconFiles(icon.PackId, icon.Id);
				await _mediator.Publish(new IconDeletedNotification(icon.Id, icon.PackId));
			}
		}

		return Result.Ok<int, IconError>(icons.Count);
	}

	public async Task<Result<IconImageResult, IconError>> GetImage(Guid iconId,
		int? size,
		bool acceptWebp,
		bool staticFrame,
		CancellationToken cancellationToken)
	{
		var icon = _iconPackCache.GetIconById(iconId);
		if (icon is null)
		{
			return Result.Fail<IconImageResult, IconError>(IconError.NotFound);
		}

		if (icon.ProcessingState != IconProcessingState.Ready)
		{
			return Result.Fail<IconImageResult, IconError>(IconError.NotReady);
		}

		var variant = IconVariants.Resolve(size, icon.AvailableSizes);
		if (!acceptWebp || (staticFrame && icon.IsAnimated))
		{
			var fallback = await _fallbackStore.GetOrCreate(icon, variant, staticFrame, cancellationToken);
			if (fallback is not null)
			{
				var frameTag = staticFrame && icon.IsAnimated ? "-static" : string.Empty;
				var fallbackTag = $"\"{IconEtagIdentity(icon)}-{variant}{frameTag}{fallback.FileExtension}\"";
				return Result.Ok<IconImageResult, IconError>(new IconImageResult(fallback.Content,
					fallbackTag,
					fallback.ContentType));
			}
		}

		var stream = _storage.OpenVariant(icon.PackId, icon.Id, variant);
		if (stream is null && variant != IconVariants.Master)
		{
			variant = IconVariants.Master;
			stream = _storage.OpenVariant(icon.PackId, icon.Id, variant);
		}

		if (stream is null)
		{
			return Result.Fail<IconImageResult, IconError>(IconError.StorageFailure, "Icon file is missing");
		}

		var etag = $"\"{IconEtagIdentity(icon)}-{variant}\"";
		return Result.Ok<IconImageResult, IconError>(new IconImageResult(stream, etag));
	}

	private bool IsPackReadOnly(Guid packId)
		=> _iconPackCache.GetPackById(packId) is { } pack && _ownerRegistry.IsReadOnly(pack);

	private static string IconEtagIdentity(IconEntity icon)
		=> icon.MasterContentHash ?? icon.SourceContentHash ?? icon.Id.ToString("N");
}
