namespace MacroDeckHost.Domain.Enums;

public enum BackupOperationStage
{
	Idle,
	Preparing,
	CreatingSnapshot,
	Encrypting,
	Saving,
	Validating,
	Restoring,
	Completed,
	Failed
}
