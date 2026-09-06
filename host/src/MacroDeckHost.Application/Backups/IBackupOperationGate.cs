using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public interface IBackupOperationLease : IDisposable
{
	Guid OperationId { get; }
}

/// <summary>
/// Serialises every backup and restore. The per-trigger policy is deliberate: a trigger whose failure has
/// to block something destructive waits for the lease, while a trigger that can safely be represented by
/// the backup already running is skipped rather than queued.
/// </summary>
public interface IBackupOperationGate
{
	bool RestorePending { get; set; }

	bool IsBusy { get; }

	Result<IBackupOperationLease, BackupError> TryAcquire(BackupOperationKind kind, BackupTrigger trigger);

	Task<Result<IBackupOperationLease, BackupError>> Acquire(BackupOperationKind kind,
		BackupTrigger trigger,
		TimeSpan timeout,
		CancellationToken cancellationToken = default);
}
