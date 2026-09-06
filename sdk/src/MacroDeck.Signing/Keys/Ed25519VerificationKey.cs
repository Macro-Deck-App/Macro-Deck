using NSec.Cryptography;

namespace MacroDeck.Signing.Keys;

/// <summary>
/// An imported Ed25519 public key, ready to verify signatures. The only way to obtain one is
/// <see cref="TryImport"/>, and the only operation exposed is <see cref="Verify"/> - callers never see the
/// underlying <see cref="NSec.Cryptography.PublicKey"/>.
/// </summary>
public sealed class Ed25519VerificationKey
{
	private readonly PublicKey _key;

	private Ed25519VerificationKey(PublicKey key)
	{
		_key = key;
	}

	/// <summary>Imports <paramref name="rawPublicKey"/>, or returns <see langword="null"/> when it is not
	/// exactly <see cref="Ed25519KeyPair.PublicKeyLength"/> bytes.</summary>
	public static Ed25519VerificationKey? TryImport(ReadOnlySpan<byte> rawPublicKey)
	{
		if (rawPublicKey.Length != Ed25519KeyPair.PublicKeyLength)
		{
			return null;
		}

		return new Ed25519VerificationKey(PublicKey.Import(SignatureAlgorithm.Ed25519,
			rawPublicKey,
			KeyBlobFormat.RawPublicKey));
	}

	/// <summary>True when <paramref name="signature"/> is a valid Ed25519 signature over
	/// <paramref name="payload"/> made by the matching private key.</summary>
	public bool Verify(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
	{
		return SignatureAlgorithm.Ed25519.Verify(_key, payload, signature);
	}
}
