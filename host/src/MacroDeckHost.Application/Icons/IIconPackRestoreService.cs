using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public interface IIconPackRestoreService
{
	Task<Result<IconPackEntity, IconError>> RestoreAsNewPack(string fileName,
		Stream content,
		CancellationToken cancellationToken);

	/// <summary>Replaces an installed pack's contents with a newer version of the same pack, keeping the
	/// pack id and the ids of icons the new version still carries, so buttons referencing them keep
	/// resolving. An icon the new version dropped is retained rather than deleted, because a button may
	/// still reference it.</summary>
	Task<Result<IconPackEntity, IconError>> UpgradePack(Guid packId,
		string fileName,
		Stream content,
		CancellationToken cancellationToken);

	Task<Result<IReadOnlyList<IconEntity>, IconError>> MergeIntoPack(Guid packId,
		Guid importBatchId,
		string fileName,
		Stream content,
		CancellationToken cancellationToken);
}
