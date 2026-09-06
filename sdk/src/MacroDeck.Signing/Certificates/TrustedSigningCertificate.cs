using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Certificates;

/// <summary>
/// A <see cref="SigningCertificate"/> that has already passed <see cref="SigningCertificateChain.Verify(byte[], byte[], string)"/>:
/// its bytes verified against the Macro Deck root key, its shape and key usage were checked, but its
/// validity window was not - callers evaluate that separately, at whatever instant the operation actually
/// cares about, with <see cref="SigningCertificateChain.EnsureValidAt"/>.
/// </summary>
public sealed class TrustedSigningCertificate
{
	private readonly Ed25519VerificationKey _publicKey;

	internal TrustedSigningCertificate(SigningCertificate certificate, Ed25519VerificationKey publicKey)
	{
		Certificate = certificate;
		_publicKey = publicKey;
	}

	/// <summary>The trusted certificate document.</summary>
	public SigningCertificate Certificate { get; }

	/// <summary>Verifies <paramref name="signature"/> over <paramref name="payload"/> against this
	/// certificate's public key. Revocation is never consulted here or anywhere in this library.</summary>
	public bool VerifySignature(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
	{
		return _publicKey.Verify(payload, signature);
	}
}
