using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Icons;

public class IconPackService : IIconPackService
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IconImportBatchTracker _batchTracker;
	private readonly IIconStorage _storage;
	private readonly IMediator _mediator;
	private readonly IIconPackOwnerRegistry _ownerRegistry;
	private readonly ILogger _logger;

	public IconPackService(
		IIconPackCache iconPackCache,
		IconImportBatchTracker batchTracker,
		IIconStorage storage,
		IMediator mediator,
		IIconPackOwnerRegistry ownerRegistry,
		ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_batchTracker = batchTracker;
		_storage = storage;
		_mediator = mediator;
		_ownerRegistry = ownerRegistry;
		_logger = logger;
	}

	public async Task<Result<IconPackEntity, IconPackError>> Create(string name,
		string? description,
		string? author,
		string? version)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<IconPackEntity, IconPackError>(IconPackError.ValidationError, "A name is required");
		}

		var pack = new IconPackEntity
		{
			Id = Guid.CreateVersion7(),
			Name = name.Trim(),
			Description = NormalizeOptional(description),
			Author = NormalizeOptional(author),
			Version = NormalizeOptional(version),
			CreatedAt = DateTime.UtcNow
		};

		await _iconPackCache.AddOrUpdatePack(pack);
		await _mediator.Publish(new IconPackCreatedNotification(pack, 0));
		return Result.Ok<IconPackEntity, IconPackError>(pack);
	}

	public async Task<Result<IconPackEntity, IconPackError>> Update(Guid id,
		string name,
		string? description,
		string? author,
		string? version)
	{
		var pack = _iconPackCache.GetPackById(id);
		if (pack is null)
		{
			return Result.Fail<IconPackEntity, IconPackError>(IconPackError.NotFound);
		}

		if (pack.IsReadOnly)
		{
			return Result.Fail<IconPackEntity, IconPackError>(IconPackError.ReadOnly,
				"Read-only icon packs cannot be edited");
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<IconPackEntity, IconPackError>(IconPackError.ValidationError, "A name is required");
		}

		pack.Name = name.Trim();
		pack.Description = NormalizeOptional(description);
		pack.Author = NormalizeOptional(author);
		pack.Version = NormalizeOptional(version);
		await _iconPackCache.AddOrUpdatePack(pack);
		await _mediator.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(pack.Id)));
		return Result.Ok<IconPackEntity, IconPackError>(pack);
	}

	public async Task<Result<IconPackError>> Delete(Guid id)
	{
		var pack = _iconPackCache.GetPackById(id);
		if (pack is null)
		{
			return Result.Fail(IconPackError.NotFound);
		}

		if (pack.IsReadOnly)
		{
			return Result.Fail(IconPackError.ReadOnly, "Read-only icon packs cannot be deleted");
		}

		if (pack.IsDefault)
		{
			return Result.Fail(IconPackError.DefaultPackProtected,
				"The default icon pack cannot be deleted");
		}

		var owner = _ownerRegistry.Resolve(pack);
		if (owner is not null)
		{
			var descriptor = owner.Describe(pack);
			if (!descriptor.CanRemove)
			{
				return Result.Fail(IconPackError.OwnedBySource,
					"This icon pack is managed by its source and cannot be deleted directly");
			}

			try
			{
				await owner.Release(pack);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Icon pack owner failed to release pack {PackId}", id);
				return Result.Fail(IconPackError.InternalError,
					"The icon pack's owner could not release it");
			}
		}

		foreach (var batch in _batchTracker.GetAll().Where(b => b.PackId == id))
		{
			_batchTracker.TryFinish(batch.Id);
			_storage.CleanupBatchStaging(batch.Id);
		}

		await _iconPackCache.RemovePack(id);
		await _mediator.Publish(new IconPackDeletedNotification(id));
		return Result.Ok<IconPackError>();
	}

	private static string? NormalizeOptional(string? value)
		=> string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
