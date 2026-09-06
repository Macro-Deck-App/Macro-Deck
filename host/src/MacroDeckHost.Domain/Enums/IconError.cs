namespace MacroDeckHost.Domain.Enums;

public enum IconError
{
	ValidationError,
	NotFound,
	PackNotFound,
	PackReadOnly,
	UnsupportedFormat,
	InvalidArchive,
	StorageFailure,
	ProcessingFailed,
	NotReady,
	InternalError
}
