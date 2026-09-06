using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public sealed record IconImportFile(string FileName, Stream Content);

public sealed record IconPackImportResult(IconImportBatchEntity? Batch, IReadOnlyList<IconPackEntity> RestoredPacks);

public sealed record SingleIconImportResult(IconEntity Icon, bool Reused);

public interface IIconImportService
{
	Task<Result<IconImportBatchEntity, IconError>> Import(Guid? packId,
		string? sourceName,
		IAsyncEnumerable<IconImportFile> files,
		CancellationToken cancellationToken);

	Task<Result<IconImportBatchEntity, IconError>> ImportFromPath(Guid? packId,
		IReadOnlyList<string> paths,
		CancellationToken cancellationToken);

	Task<Result<SingleIconImportResult, IconError>> ImportSingle(Guid? packId,
		IconImportFile file,
		CancellationToken cancellationToken);

	Task<Result<IconPackImportResult, IconError>> ImportPacks(IAsyncEnumerable<IconImportFile> files,
		CancellationToken cancellationToken);

	Task<bool> CancelBatch(Guid batchId, CancellationToken cancellationToken);
}
