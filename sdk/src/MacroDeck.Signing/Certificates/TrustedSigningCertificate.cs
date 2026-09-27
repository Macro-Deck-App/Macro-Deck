using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Certificates;

/// <summary>
/// A <see cref="SigningCertificate"/> that has already passed
/// <see cref="SigningCertificateChain.Verify(byte[], byte[], byte[], byte[], ReadOnlySpan{byte}, string)"/>: its
/// bytes verified against the Macro Deck root key or against an issuer certificate the root signed, its shape and
/// key usage were checked, but its validity window was not - callers evaluate that separately, at whatever instant
/// the operation actually cares about, with
/// <see cref="SigningCertificateChain.EnsureValidAt(TrustedSigningCertificate, DateTimeOffset)"/>.
/// </summary>
public sealed class TrustedSigningCertificate
{
	private readonly Ed25519VerificationKey _publicKey;

	internal TrustedSigningCertificate(SigningCertificate certificate,
		Ed25519VerificationKey publicKey,
		SigningCertificate? issuer = null)
	{
		Certificate = certificate;
		_publicKey = publicKey;
		Issuer = issuer;
	}

	/// <summary>The trusted certificate document.</summary>
	public SigningCertificate Certificate { get; }

	/// <summary>The issuer certificate that signed <see cref="Certificate"/>, already verified against the root,
	/// or <see langword="null"/> when the root signed <see cref="Certificate"/> directly.</summary>
	public SigningCertificate? Issuer { get; }

	internal Ed25519VerificationKey PublicKey => _publicKey;

	/// <summary>Verifies <paramref name="signature"/> over <paramref name="payload"/> against this
	/// certificate's public key. Revocation is never consulted here or anywhere in this library.</summary>
	public bool VerifySignature(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
	{
		return _publicKey.Verify(payload, signature);
	}
}
