using MacroDeckHost.Application.Security.KeyRing;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

/// <summary>
/// Used where the platform offers no keystore. It stores nothing anywhere - in particular not under the
/// data root, which would hand an attacker the key beside the ring it unlocks - so the key ring stays
/// readable exactly as it was before this feature existed. The state is reported rather than silent.
/// </summary>
public sealed class NullKekStore : IKekStore
{
	private readonly string _reason;

	public NullKekStore(string reason) => _reason = reason;

	public KeyRingBackend Backend => KeyRingBackend.None;

	public KekStoreAvailability Availability => new(false, _reason);

	public KekStoreReadResult Read(KekStoreIdentity identity) => KekStoreReadResult.Unavailable(_reason);

	public KekStoreStatus Write(KekStoreIdentity identity, ReadOnlySpan<byte> kek) => KekStoreStatus.Unavailable;

	public KekStoreStatus Delete(KekStoreIdentity identity) => KekStoreStatus.Unavailable;
}
