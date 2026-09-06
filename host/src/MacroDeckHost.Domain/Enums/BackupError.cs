namespace MacroDeckHost.Domain.Enums;

public enum BackupError
{
	NotFound,
	ProviderUnavailable,
	StorageFailure,
	InsufficientDiskSpace,
	InvalidArchive,
	UnsupportedVersion,
	UnsupportedEncryption,
	IntegrityFailure,
	RecoveryKeyMissing,
	RecoveryKeyRequired,
	RecoveryKeyInvalid,
	Busy,
	Cancelled,
	SnapshotFailed,
	RestoreStagingFailed,
	RestorePending,
	RestartUnavailable,
	ValidationError,
	TooLarge,
	DesktopOnly,
	FileLocked
}
