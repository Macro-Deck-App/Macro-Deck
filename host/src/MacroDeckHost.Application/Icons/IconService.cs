using System.Globalization;
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
	private readonly IIconVariantDeriver _variantDeriver;
	private readonly IconImportCoalescer _coalescer;
	private readonly IMediator _mediator;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public IconService(IIconPackCache iconPackCache,
		IIconStorage storage,
		IIconImageFallbackStore fallbackStore,
		IIconVariantDeriver variantDeriver,
		IconImportCoalescer coalescer,
		IMediator mediator,
		IIconPackOwnerRegistry ownerRegistry)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_fallbackStore = fallbackStore;
		_variantDeriver = variantDeriver;
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
		await _iconPackCache.ForgetSourceRevision(icon.PackId);
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
		await _iconPackCache.ForgetSourceRevision(icon.PackId);
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
			await _iconPackCache.ForgetSourceRevision(packGroup.Key);
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

		// One snapshot labels the response: a pack upgrade replaces these fields on the live entity mid-request.
		var masterContentHash = icon.MasterContentHash;
		var version = IconImageVersion.Of(icon);
		var identity = IconEtagIdentity(icon);

		var variant = IconVariants.Resolve(size, icon.AvailableSizes.ToList());
		var storageName = variant;
		var stream = variant == IconVariants.Master ? null : _storage.OpenVariant(icon.PackId, icon.Id, variant);
		if (stream is null)
		{
			variant = storageName = IconVariants.Master;
		}

		if (variant == IconVariants.Master && size is not null && masterContentHash is not null)
		{
			var target = IconVariants.Resolve(size, IconVariants.TargetSizes);
			if (target != IconVariants.Master)
			{
				var derived = await _variantDeriver.GetOrCreate(icon,
					masterContentHash,
					int.Parse(target, CultureInfo.InvariantCulture),
					cancellationToken);
				if (derived.Outcome == DerivedVariantOutcome.Derived)
				{
					variant = target;
					storageName = derived.StorageName!;
					stream = derived.Content;
				}
				else if (derived.Outcome == DerivedVariantOutcome.Unavailable)
				{
					version = null;
				}
			}
		}

		if (!acceptWebp || (staticFrame && icon.IsAnimated))
		{
			var fallback = await _fallbackStore.GetOrCreate(icon, variant, staticFrame, cancellationToken, storageName);
			if (fallback is not null)
			{
				if (stream is not null)
				{
					await stream.DisposeAsync();
				}

				var frameTag = staticFrame && icon.IsAnimated ? "-static" : string.Empty;
				var fallbackTag = $"\"{identity}-{variant}{frameTag}{fallback.FileExtension}\"";
				return Result.Ok<IconImageResult, IconError>(new IconImageResult(fallback.Content,
					fallbackTag,
					fallback.ContentType,
					version));
			}
		}

		stream ??= _storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
		if (stream is null)
		{
			return Result.Fail<IconImageResult, IconError>(IconError.StorageFailure, "Icon file is missing");
		}

		var etag = $"\"{identity}-{variant}\"";
		return Result.Ok<IconImageResult, IconError>(new IconImageResult(stream, etag, Version: version));
	}

	private bool IsPackReadOnly(Guid packId)
		=> _iconPackCache.GetPackById(packId) is { } pack && _ownerRegistry.IsReadOnly(pack);

	private static string IconEtagIdentity(IconEntity icon)
		=> icon.MasterContentHash ?? icon.SourceContentHash ?? icon.Id.ToString("N");
}
