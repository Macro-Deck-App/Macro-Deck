namespace MacroDeckHost.Domain.Enums;

/// <summary>
/// How far the source got with the encrypted credentials it found. Drives the choice the wizard offers:
/// anything other than <see cref="Decrypted" /> or <see cref="NotPresent" /> means the user must either
/// supply the key or accept that only unencrypted data migrates.
/// </summary>
public enum MigrationCredentialStatus
{
	NotPresent,
	Decrypted,
	KeyUnavailable,
	KeyRejected,
	Skipped
}
