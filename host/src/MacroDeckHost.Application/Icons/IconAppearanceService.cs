using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Icons;

public sealed record IconAppearanceMergeResult(IconEntity Icon, Guid MergedIconId);

public interface IIconAppearanceService
{
	Task<Result<IconEntity, IconError>> AddOrReplace(Guid iconId,
		string key,
		IconImportFile file,
		CancellationToken cancellationToken);

	Task<Result<IconEntity, IconError>> Remove(Guid iconId, Guid appearanceId, CancellationToken cancellationToken);

	Task<Result<IconAppearanceMergeResult, IconError>> Merge(Guid targetIconId,
		Guid sourceIconId,
		string key,
		CancellationToken cancellationToken);
}

public sealed class IconAppearanceService : IIconAppearanceService
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IconImportBatchTracker _batchTracker;
	private readonly IconImportBatchFinalizer _batchFinalizer;
	private readonly IconProcessingChannel _processingChannel;
	private readonly IconImportCoalescer _coalescer;
	private readonly IIconPackOwnerRegistry _ownerRegistry;
	private readonly IIconReferenceRewriter _referenceRewriter;
	private readonly IUiTransport _uiTransport;
	private readonly IMediator _mediator;
	private readonly IconAppearanceEditLocks _editLocks;

	public IconAppearanceService(IIconPackCache iconPackCache,
		IIconStorage storage,
		IconImportBatchTracker batchTracker,
		IconImportBatchFinalizer batchFinalizer,
		IconProcessingChannel processingChannel,
		IconImportCoalescer coalescer,
		IIconPackOwnerRegistry ownerRegistry,
		IIconReferenceRewriter referenceRewriter,
		IUiTransport uiTransport,
		IMediator mediator,
		IconAppearanceEditLocks editLocks)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_batchTracker = batchTracker;
		_batchFinalizer = batchFinalizer;
		_processingChannel = processingChannel;
		_coalescer = coalescer;
		_ownerRegistry = ownerRegistry;
		_referenceRewriter = referenceRewriter;
		_uiTransport = uiTransport;
		_mediator = mediator;
		_editLocks = editLocks;
	}

	public async Task<Result<IconEntity, IconError>> AddOrReplace(Guid iconId,
		string key,
		IconImportFile file,
		CancellationToken cancellationToken)
	{
		await using var editLock = await _editLocks.Acquire([iconId], cancellationToken);
		var (parent, error) = FindEditableParent(iconId);
		if (parent is null)
		{
			return Result.Fail<IconEntity, IconError>(error!.Value.Error, error.Value.Message);
		}

		if (!IconAppearanceTraits.TryParse(key, out var traits))
		{
			return Result.Fail<IconEntity, IconError>(IconError.ValidationError, "The appearance key is not valid");
		}

		if (!IconImportFiles.IsSupportedImportEntry(file.FileName))
		{
			return Result.Fail<IconEntity, IconError>(IconError.UnsupportedFormat,
				$"{Path.GetFileName(file.FileName)} is not a supported image");
		}

		var canonicalKey = IconAppearanceTraits.ToKey(traits);
		var appearances = _iconPackCache.GetAppearances(parent.Id);
		var replaced = appearances.FirstOrDefault(asset => KeyOf(asset) == canonicalKey);
		if (replaced is null && appearances.Count >= IconAppearanceTraits.MaxAppearancesPerIcon)
		{
			return Result.Fail<IconEntity, IconError>(IconError.ValidationError,
				"An icon holds at most eight appearances");
		}

		var batch = new IconImportBatchEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = parent.PackId,
			SourceName = Path.GetFileName(file.FileName),
			State = IconImportBatchState.Discovering,
			ExplicitDestination = true,
			Mode = IconImportMode.Destination,
			Silent = true,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};

		var assetId = Guid.CreateVersion7();
		var sourceHash = await _storage.StageOriginal(batch.Id, assetId, file.FileName, file.Content, cancellationToken);

		if (replaced is not null)
		{
			await RemoveAsset(replaced);
		}

		var asset = new IconEntity
		{
			Id = assetId,
			PackId = parent.PackId,
			Name = parent.Name,
			SourceContentHash = sourceHash.Value,
			OriginalFileName = file.FileName,
			ProcessingState = IconProcessingState.Pending,
			ImportBatchId = batch.Id,
			AppearanceOfId = parent.Id,
			AppearanceTraits = new Dictionary<string, string>(traits, StringComparer.Ordinal),
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};

		_batchTracker.Register(batch);
		await _iconPackCache.AddIcons(parent.PackId, [asset]);
		await _iconPackCache.ForgetSourceRevision(parent.PackId);
		_batchTracker.AddToTotal(batch.Id, [asset]);
		batch.State = IconImportBatchState.Processing;
		batch.Total = 1;
		_batchTracker.Persist(batch);
		_processingChannel.Enqueue(new ProcessIconWorkItem(asset.Id));

		await _mediator.Publish(new IconUpdatedNotification(parent), cancellationToken);
		await _batchFinalizer.TryFinalize(batch.Id, _mediator, cancellationToken);
		return Result.Ok<IconEntity, IconError>(parent);
	}

	public async Task<Result<IconEntity, IconError>> Remove(Guid iconId,
		Guid appearanceId,
		CancellationToken cancellationToken)
	{
		await using var editLock = await _editLocks.Acquire([iconId], cancellationToken);
		var (parent, error) = FindEditableParent(iconId);
		if (parent is null)
		{
			return Result.Fail<IconEntity, IconError>(error!.Value.Error, error.Value.Message);
		}

		if (_iconPackCache.GetIconById(appearanceId) is not { } asset || asset.AppearanceOfId != parent.Id)
		{
			return Result.Fail<IconEntity, IconError>(IconError.NotFound);
		}

		await RemoveAsset(asset);
		await _iconPackCache.ForgetSourceRevision(parent.PackId);
		await _mediator.Publish(new IconUpdatedNotification(parent), cancellationToken);
		return Result.Ok<IconEntity, IconError>(parent);
	}

	public async Task<Result<IconAppearanceMergeResult, IconError>> Merge(Guid targetIconId,
		Guid sourceIconId,
		string key,
		CancellationToken cancellationToken)
	{
		await using var editLock = await _editLocks.Acquire([targetIconId, sourceIconId], cancellationToken);
		var (target, error) = FindEditableParent(targetIconId);
		if (target is null)
		{
			return Result.Fail<IconAppearanceMergeResult, IconError>(error!.Value.Error, error.Value.Message);
		}

		if (_iconPackCache.GetIconById(sourceIconId) is not { AppearanceOfId: null } source)
		{
			return Result.Fail<IconAppearanceMergeResult, IconError>(IconError.NotFound);
		}

		if (source.Id == target.Id || source.PackId != target.PackId)
		{
			return Result.Fail<IconAppearanceMergeResult, IconError>(IconError.ValidationError,
				"Only another icon of the same pack can become an appearance");
		}

		if (_iconPackCache.GetAppearances(source.Id).Count > 0)
		{
			return Result.Fail<IconAppearanceMergeResult, IconError>(IconError.ValidationError,
				"An icon that has appearances of its own cannot become an appearance");
		}

		if (!IconAppearanceTraits.TryParse(key, out var traits))
		{
			return Result.Fail<IconAppearanceMergeResult, IconError>(IconError.ValidationError,
				"The appearance key is not valid");
		}

		var canonicalKey = IconAppearanceTraits.ToKey(traits);
		var appearances = _iconPackCache.GetAppearances(target.Id);
		var replaced = appearances.FirstOrDefault(asset => KeyOf(asset) == canonicalKey);
		if (replaced is null && appearances.Count >= IconAppearanceTraits.MaxAppearancesPerIcon)
		{
			return Result.Fail<IconAppearanceMergeResult, IconError>(IconError.ValidationError,
				"An icon holds at most eight appearances");
		}

		if (replaced is not null)
		{
			await RemoveAsset(replaced);
		}

		source.AppearanceOfId = target.Id;
		source.Name = target.Name;
		source.AppearanceTraits = new Dictionary<string, string>(traits, StringComparer.Ordinal);
		await _iconPackCache.UpdateIcon(source);
		await _iconPackCache.ForgetSourceRevision(target.PackId);

		await _referenceRewriter.Replace(source.Id, target.Id, cancellationToken);

		await _uiTransport.Send(new IconDeletedEvent
			{
				IconId = source.Id.ToString(),
				PackId = source.PackId.ToString()
			},
			cancellationToken);
		await _mediator.Publish(new IconUpdatedNotification(target), cancellationToken);
		if (_iconPackCache.GetPackById(target.PackId) is { } pack)
		{
			await _mediator.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(pack.Id)),
				cancellationToken);
		}

		return Result.Ok<IconAppearanceMergeResult, IconError>(new IconAppearanceMergeResult(target, source.Id));
	}

	private (IconEntity? Parent, (IconError Error, string? Message)? Error) FindEditableParent(Guid iconId)
	{
		if (_iconPackCache.GetIconById(iconId) is not { AppearanceOfId: null } parent)
		{
			return (null, (IconError.NotFound, null));
		}

		if (_iconPackCache.GetPackById(parent.PackId) is not { } pack)
		{
			return (null, (IconError.PackNotFound, null));
		}

		return _ownerRegistry.IsReadOnly(pack)
			? (null, (IconError.PackReadOnly, "Icons in read-only packs cannot be changed"))
			: (parent, null);
	}

	private async Task RemoveAsset(IconEntity asset)
	{
		await _iconPackCache.RemoveIcon(asset.Id);
		_coalescer.ReleaseAll(asset.Id);
		_storage.DeleteIconFiles(asset.PackId, asset.Id);
	}

	private static string KeyOf(IconEntity asset)
		=> asset.AppearanceTraits is { } traits ? IconAppearanceTraits.ToKey(traits) : string.Empty;
}
