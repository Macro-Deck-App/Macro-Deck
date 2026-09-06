using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Security.KeyRing;

public enum KeyRingBackend
{
	None,
	WindowsCredentialManager,
	MacOsKeychain,
	LinuxSecretService
}

public enum KeyRingProtectionState
{
	Unprotected,
	Protected,
	Locked
}

/// <summary>Why a wrapped ring cannot be opened. Distinct reasons because the remedies differ.</summary>
public enum KeyRingLockReason
{
	None,
	KeystoreEntryMissing,
	KeystoreEntryStale,
	KeystoreUnavailable,
	EscrowMissing
}

/// <summary>Why a readable ring has not been wrapped. Surfaced so the fallback is never silent.</summary>
public enum KeyRingUnprotectedReason
{
	None,
	RecoveryKeyNotExported,
	NoKeystore,
	Portable
}

public enum KeyRingProtectionError
{
	KeystoreUnavailable,
	EscrowMissing,
	EscrowUnreadable,
	RecoveryKeyInvalid,
	Locked,
	MigrationFailed
}

public sealed record KeyRingProtectionStatus(
	KeyRingProtectionState State,
	KeyRingLockReason LockReason,
	KeyRingUnprotectedReason UnprotectedReason,
	KeyRingBackend Backend,
	bool BackendAvailable,
	string? BackendUnavailableReason,
	string? KekId,
	int EscrowWrapCount,
	bool RecoveryKeyExported,
	bool MigrationPending);

public interface IKeyRingProtectionService
{
	KeyRingProtectionStatus Status { get; }

	/// <summary>
	/// Wraps the key ring if it is not wrapped yet, and always leaves a wrap the supplied recovery key
	/// can open. Safe to call repeatedly; a ring already protected under the same key is left alone.
	/// </summary>
	Task<Result<KeyRingProtectionError>> EnsureProtected(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Adds a wrap without disturbing the ones already there, so a regenerated recovery key cannot
	/// strand an installation before its replacement has been written down.
	/// </summary>
	Task<Result<KeyRingProtectionError>> AddEscrowWrap(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default);

	Task<Result<KeyRingProtectionError>> PruneEscrowWrapsExcept(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Recovers the key encryption key from the escrow with a recovery key the user typed, and puts it
	/// back in the keystore. Deliberately independent of the database: the secrets there are exactly
	/// what is unreadable when this is needed.
	/// </summary>
	Task<Result<KeyRingProtectionError>> Unlock(ReadOnlyMemory<byte> recoveryKey,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Wraps any key file still readable on an already-protected ring. Needs no recovery key, because the
	/// key encryption key is already in hand - which matters, since the usual cause is a key Data
	/// Protection rotated in a session that became protected after its provider was built.
	/// </summary>
	Task<Result<KeyRingProtectionError>> RewrapPending(CancellationToken cancellationToken = default);

	/// <summary>
	/// Takes over a key encryption key recovered from a restored archive's escrow, so a restore onto a
	/// machine that has never seen this installation does not come up locked asking for the recovery key
	/// the user just supplied for the archive.
	/// </summary>
	Result<KeyRingProtectionError> AdoptKek(ReadOnlyMemory<byte> kek);
}
