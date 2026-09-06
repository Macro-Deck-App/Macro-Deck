namespace MacroDeckHost.Application.Backups;

public interface IBackupCatalog
{
	Task<IReadOnlyList<BackupDescriptor>> List(CancellationToken cancellationToken = default);

	Task<BackupDescriptor?> Find(Guid backupId, CancellationToken cancellationToken = default);

	Task Invalidate();
}
