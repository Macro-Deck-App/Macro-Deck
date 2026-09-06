namespace MacroDeckHost.Domain.Enums;

public enum PortabilityError
{
	NotFound,
	IsVirtual,
	InvalidArchive,
	UnsupportedVersion,
	PasswordRequired,
	InvalidPassword,
	WeakPassword,
	ValidationError,
	StorageFailure,
	DesktopOnly
}
