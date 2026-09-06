using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

/// <summary>
/// A stand-in for a real keystore that can be put into each of the three read states, and that records
/// writes so a test can assert nothing was stored.
/// </summary>
internal sealed class FakeKekStore : IKekStore
{
	private byte[]? _kek;

	public KeyRingBackend Backend { get; init; } = KeyRingBackend.MacOsKeychain;

	public KekStoreAvailability Availability { get; set; } = new(true, null);

	public KekStoreStatus? ReadOverride { get; set; }

	public bool WriteFails { get; set; }

	public int Writes { get; private set; }

	public byte[]? Stored => _kek?.ToArray();

	public void Seed(byte[] kek) => _kek = kek.ToArray();

	public void Forget() => _kek = null;

	public KekStoreReadResult Read(KekStoreIdentity identity)
	{
		if (ReadOverride == KekStoreStatus.Unavailable)
		{
			return KekStoreReadResult.Unavailable(Availability.Reason ?? "unavailable");
		}

		if (ReadOverride == KekStoreStatus.NotFound || _kek is null)
		{
			return KekStoreReadResult.NotFound();
		}

		return KekStoreReadResult.Found(_kek.ToArray());
	}

	public KekStoreStatus Write(KekStoreIdentity identity, ReadOnlySpan<byte> kek)
	{
		if (WriteFails)
		{
			return KekStoreStatus.Unavailable;
		}

		Writes++;
		_kek = kek.ToArray();

		return KekStoreStatus.Found;
	}

	public KekStoreStatus Delete(KekStoreIdentity identity)
	{
		var existed = _kek is not null;
		_kek = null;

		return existed ? KekStoreStatus.Found : KekStoreStatus.NotFound;
	}
}
