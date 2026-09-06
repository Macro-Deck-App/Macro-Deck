using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed record CreateBackupRequest(BackupTrigger Trigger, string? Note = null, bool Protected = false);

public sealed record BackupSourceRef(string? ProviderId, string? StorageId, string? FilePath);

public sealed record BackupExportHandle(Stream Content, string FileName, long Length) : IDisposable
{
	public void Dispose() => Content.Dispose();
}

public interface IBackupService
{
	Task<Result<BackupDescriptor, BackupError>> Create(CreateBackupRequest request,
		CancellationToken cancellationToken = default);

	Task<Result<IReadOnlyList<BackupDescriptor>, BackupError>> List(CancellationToken cancellationToken = default);

	Task<Result<BackupInspection, BackupError>> Inspect(BackupSourceRef source,
		string? recoveryKey,
		CancellationToken cancellationToken = default);

	Task<Result<BackupDescriptor, BackupError>> Import(BackupSourceRef source,
		CancellationToken cancellationToken = default);

	Task<Result<BackupExportHandle, BackupError>> OpenExport(Guid backupId,
		CancellationToken cancellationToken = default);

	Task<Result<BackupError>> Delete(Guid backupId, CancellationToken cancellationToken = default);
}
