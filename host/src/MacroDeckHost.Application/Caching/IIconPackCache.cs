using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Application.Caching;

public interface IIconPackCache
{
	Task InitializeCache();

	IconPackEntity? GetPackById(Guid id);

	List<IconPackEntity> GetAllPacks();

	IconPackEntity? GetDefaultPack();

	Task AddOrUpdatePack(IconPackEntity pack);

	Task RemovePack(Guid id);

	IconEntity? GetIconById(Guid iconId);

	List<IconEntity> GetIconsByPackId(Guid packId);

	List<IconEntity> GetIconsByBatchId(Guid batchId);

	List<IconEntity> GetIconsByState(params IconProcessingState[] states);

	int GetIconCount(Guid packId);

	IconEntity? FindBySourceContentHash(SourceContentHash hash, Guid? withinPackId = null);

	IconEntity? FindByMasterContentHash(MasterContentHash hash, Guid? withinPackId = null);

	List<IconEntity> GetIconsMissingMasterContentHash();

	Task AddIcons(Guid packId, IReadOnlyList<IconEntity> icons);

	Task UpdateIcon(IconEntity icon);

	Task RemoveIcon(Guid iconId);

	Task RemoveIcons(Guid packId, IReadOnlyList<Guid> iconIds);

	Task FlushPendingWrites();
}
