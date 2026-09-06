using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public sealed record BackupOperationStatus(
	Guid? OperationId,
	BackupOperationKind Kind,
	BackupOperationStage Stage,
	BackupTrigger Trigger,
	int? PercentComplete,
	long? BytesProcessed,
	long? TotalBytes,
	Guid? BackupId,
	BackupError? Error,
	string? ErrorMessage,
	DateTimeOffset UpdatedAt)
{
	public static BackupOperationStatus Idle(DateTimeOffset now)
		=> new(null,
			BackupOperationKind.Create,
			BackupOperationStage.Idle,
			BackupTrigger.Manual,
			null,
			null,
			null,
			null,
			null,
			null,
			now);
}

public interface IBackupProgressReporter
{
	BackupOperationStatus Current { get; }

	void Report(BackupOperationStatus status);
}
