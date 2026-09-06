namespace MacroDeckHost.Domain.Enums;

public enum MigrationError
{
	UnknownSource,
	SourceNotFound,
	SourceUnreadable,
	NothingToMigrate,
	DecryptionKeyRequired,
	InvalidDecryptionKey,
	KeyRingLocked,
	StorageFailure,
	DesktopOnly
}
