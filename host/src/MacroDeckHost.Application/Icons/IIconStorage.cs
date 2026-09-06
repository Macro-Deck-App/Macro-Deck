using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Application.Icons;

public interface IIconStorage
{
	Task<SourceContentHash> StageOriginal(Guid batchId,
		Guid iconId,
		string originalFileName,
		Stream content,
		CancellationToken cancellationToken);

	Task<string> StageArchive(Guid batchId, string fileName, Stream content, CancellationToken cancellationToken);

	Stream? OpenStagedOriginal(Guid batchId, Guid iconId);

	void DeleteStagedOriginal(Guid batchId, Guid iconId);

	IReadOnlyList<string> GetStagedArchivePaths(Guid batchId);

	Task WriteVariant(Guid packId,
		Guid iconId,
		string variant,
		ReadOnlyMemory<byte> webpData,
		CancellationToken cancellationToken);

	Task<string> WriteVariant(Guid packId,
		Guid iconId,
		string variant,
		Stream content,
		CancellationToken cancellationToken);

	Stream? OpenVariant(Guid packId, Guid iconId, string variant);

	void DeleteIconFiles(Guid packId, Guid iconId);

	void CleanupBatchStaging(Guid batchId);

	IReadOnlyList<Guid> EnumerateStagedBatchIds();
}
