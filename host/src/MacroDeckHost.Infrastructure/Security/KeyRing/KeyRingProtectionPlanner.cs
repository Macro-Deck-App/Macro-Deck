using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public sealed record KeyRingProtectionPlan(
	KeyRingProtectionMode Mode,
	KeyRingLockReason LockReason,
	KeyRingUnprotectedReason UnprotectedReason,
	KeyRingBackend Backend,
	bool BackendAvailable,
	string? BackendUnavailableReason,
	byte[]? Kek,
	string? RingKekId,
	bool MigrationPending);

/// <summary>
/// Decides, before the container exists, whether the ring is readable, wrapped, or wrapped with no way
/// to open it. It classifies key files by their XML alone and reads the keystore; it never decrypts and
/// never writes, so it is safe to run at the very start of a process.
/// </summary>
public static class KeyRingProtectionPlanner
{
	public static KeyRingProtectionPlan Resolve(string keysDirectory,
		IKekStore store,
		KekStoreIdentity identity,
		bool recoveryKeyExported,
		bool portable)
	{
		var availability = store.Availability;
		var states = KeyRingFileInspector.InspectDirectory(keysDirectory);
		var wrapped = states.Where(state => state.Protection == KeyRingFileProtection.KeyEncryptionKey).ToList();
		var openable = states.Any(state =>
			state.Protection is KeyRingFileProtection.Plaintext or KeyRingFileProtection.WindowsDpapi);

		if (wrapped.Count == 0)
		{
			return Unprotected(store,
				availability,
				UnprotectedBecause(availability, recoveryKeyExported, portable),
				migrationPending: openable && recoveryKeyExported && !portable && availability.Supported);
		}

		var ringKekId = wrapped[0].KekId;

		// Files wrapped under two different keys cannot all be readable, and Data Protection answers a
		// key it cannot read by minting a replacement - so a mixed ring locks rather than being trusted
		// on whichever file happened to sort first.
		if (wrapped.Any(state => !string.Equals(state.KekId, ringKekId, StringComparison.Ordinal)))
		{
			return Locked(store, availability, KeyRingLockReason.KeystoreEntryStale, ringKekId);
		}

		var read = store.Read(identity);

		switch (read.Status)
		{
			case KekStoreStatus.Found when string.Equals(KeyRingKeyDerivation.DeriveKekId(read.Kek!),
				ringKekId,
				StringComparison.Ordinal):
				return new KeyRingProtectionPlan(KeyRingProtectionMode.Protected,
					KeyRingLockReason.None,
					KeyRingUnprotectedReason.None,
					store.Backend,
					availability.Supported,
					availability.Reason,
					read.Kek,
					ringKekId,
					// A ring that still holds readable files is only half converted, which the migrator
					// finishes on the next pass rather than leaving indefinitely.
					openable);

			case KekStoreStatus.Found:
				return Locked(store, availability, KeyRingLockReason.KeystoreEntryStale, ringKekId);

			case KekStoreStatus.NotFound:
				return Locked(store,
					availability,
					KekEscrowStore.Exists(keysDirectory)
						? KeyRingLockReason.KeystoreEntryMissing
						: KeyRingLockReason.EscrowMissing,
					ringKekId);

			default:
				// The backend could not answer. Carrying on as if the entry were absent would let Data
				// Protection mint a fresh ring over a perfectly good one.
				return Locked(store, availability, KeyRingLockReason.KeystoreUnavailable, ringKekId);
		}
	}

	private static KeyRingUnprotectedReason UnprotectedBecause(KekStoreAvailability availability,
		bool recoveryKeyExported,
		bool portable)
	{
		if (portable)
		{
			return KeyRingUnprotectedReason.Portable;
		}

		if (!availability.Supported)
		{
			return KeyRingUnprotectedReason.NoKeystore;
		}

		return recoveryKeyExported ? KeyRingUnprotectedReason.None : KeyRingUnprotectedReason.RecoveryKeyNotExported;
	}

	private static KeyRingProtectionPlan Unprotected(IKekStore store,
		KekStoreAvailability availability,
		KeyRingUnprotectedReason reason,
		bool migrationPending)
		=> new(KeyRingProtectionMode.Unprotected,
			KeyRingLockReason.None,
			reason,
			store.Backend,
			availability.Supported,
			availability.Reason,
			null,
			null,
			migrationPending);

	private static KeyRingProtectionPlan Locked(IKekStore store,
		KekStoreAvailability availability,
		KeyRingLockReason reason,
		string? ringKekId)
		=> new(KeyRingProtectionMode.Locked,
			reason,
			KeyRingUnprotectedReason.None,
			store.Backend,
			availability.Supported,
			availability.Reason,
			null,
			ringKekId,
			false);
}
