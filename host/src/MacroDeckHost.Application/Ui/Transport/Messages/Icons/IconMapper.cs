using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public static class IconMapper
{
	// Deliberately required rather than defaulted: a default would let a call site compile without
	// ever consulting the owner registry, silently reporting every pack as user-created (including
	// in realtime event payloads a client cannot re-fetch to correct).
	public static IconPack ToDto(IconPackEntity entity, int iconCount, IconPackOwnerDescriptor owner)
		=> new()
		{
			Id = entity.Id.ToString(),
			Name = entity.Name,
			Description = entity.Description,
			Author = entity.Author,
			Version = entity.Version,
			IsDefault = entity.IsDefault,
			IsReadOnly = entity.IsReadOnly,
			SourceType = entity.SourceType.ToString(),
			CreatedAt = entity.CreatedAt,
			UpdatedAt = entity.UpdatedAt,
			IconCount = iconCount,
			OwnerKind = owner.Kind.ToString(),
			CanDelete = owner.CanRemove
		};

	public static ImportIconsResponse ToImportResponse(Result<IconImportBatchEntity, IconError> result,
		IconImportBatchTracker batchTracker)
	{
		var response = new ImportIconsResponse { Success = result.Success };
		if (result.Success)
		{
			var batch = result.Data!;
			var (total, processed, failed) = batchTracker.GetCounters(batch.Id);
			if (batchTracker.Get(batch.Id) is null && batch.State == IconImportBatchState.Completed)
			{
				processed = batch.Total ?? 0;
			}

			response.Batch = ToDto(batch, batch.Total ?? (total > 0 ? total : null), processed, failed);
		}
		else
		{
			response.Error = new TransportError
			{
				Code = result.Error.ToString()!,
				Message = result.ErrorMessage ?? string.Empty
			};
		}

		return response;
	}

	public static ImportSingleIconResponse ToSingleImportResponse(Result<SingleIconImportResult, IconError> result)
	{
		if (!result.Success)
		{
			return new ImportSingleIconResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = result.Error.ToString()!,
					Message = result.ErrorMessage ?? string.Empty
				}
			};
		}

		return new ImportSingleIconResponse
		{
			Success = true,
			Icon = ToDto(result.Data!.Icon),
			Reused = result.Data!.Reused
		};
	}

	public static ImportIconPacksResponse ToImportPacksResponse(Result<IconPackImportResult, IconError> result,
		IconImportBatchTracker batchTracker,
		IIconPackCache iconPackCache,
		IIconPackOwnerRegistry ownerRegistry)
	{
		if (!result.Success)
		{
			return new ImportIconPacksResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = result.Error.ToString()!,
					Message = result.ErrorMessage ?? string.Empty
				}
			};
		}

		var data = result.Data!;
		var response = new ImportIconPacksResponse
		{
			Success = true,
			Packs = data.RestoredPacks
				.Select(pack => ToDto(pack, iconPackCache.GetIconCount(pack.Id), ownerRegistry.Describe(pack)))
				.ToList()
		};
		if (data.Batch is not null)
		{
			var (total, processed, failed) = batchTracker.GetCounters(data.Batch.Id);
			response.Batch = ToDto(data.Batch, data.Batch.Total ?? (total > 0 ? total : null), processed, failed);
		}

		return response;
	}

	public static IconImportBatch ToDto(IconImportBatchEntity entity, int? total, int processed, int failed)
		=> new()
		{
			Id = entity.Id.ToString(),
			PackId = entity.PackId.ToString(),
			State = entity.State.ToString(),
			SourceName = entity.SourceName,
			Total = total,
			Processed = processed,
			Failed = failed,
			Error = entity.Error
		};

	public static Icon ToDto(IconEntity entity)
		=> new()
		{
			Id = entity.Id.ToString(),
			PackId = entity.PackId.ToString(),
			Name = entity.Name,
			Width = entity.Width,
			Height = entity.Height,
			IsAnimated = entity.IsAnimated,
			ProcessingState = entity.ProcessingState.ToString(),
			ProcessingError = entity.ProcessingError,
			AvailableSizes = entity.AvailableSizes.ToList(),
			OriginalFileName = entity.OriginalFileName,
			CreatedAt = entity.CreatedAt
		};
}
