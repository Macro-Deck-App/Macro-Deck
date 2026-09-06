namespace MacroDeckHost.Application.Store;

public enum RegistryRefreshError
{
	Disabled,
	NetworkFailure,
	Malformed,
	BudgetExceeded,
	SizeMismatch,
	SequenceRollback,
	SignatureInvalid,
	CertificateUntrusted,
	SigningKeyRevoked,
	SnapshotUnchanged,
	StorageFailure
}
