using System.Text;
using System.Text.Json;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Certificates;

/// <summary>
/// Verifies a <see cref="SigningCertificate"/> document against the Macro Deck root key. Verification is
/// always over the certificate's exact file bytes, never a re-serialized document - re-serializing, even
/// just reformatting whitespace, invalidates the root signature. Revocation is never consulted here.
/// </summary>
public static class SigningCertificateChain
{
	/// <summary>The key usage a creator or organization package-signing certificate must exclusively carry.
	/// </summary>
	public const string PackageKeyUsage = "package";

	/// <summary>The key usage Macro Deck's own registry-signing certificate must exclusively carry.
	/// </summary>
	public const string RegistryKeyUsage = "registry";

	/// <summary>Verifies <paramref name="certificateBytes"/> against <see cref="MacroDeckRootKey"/>.
	/// </summary>
	public static SigningCertificateVerificationResult Verify(byte[] certificateBytes,
		byte[] certificateSignatureFile,
		string requiredKeyUsage)
	{
		return Verify(certificateBytes, certificateSignatureFile, MacroDeckRootKey.PublicKey, requiredKeyUsage);
	}

	/// <summary>
	/// Verifies <paramref name="certificateBytes"/> against <paramref name="rootPublicKey"/>: that
	/// <paramref name="certificateSignatureFile"/> is a valid Ed25519 signature over the certificate's exact
	/// bytes, that the document has the expected shape and algorithm, that it carries exactly
	/// <paramref name="requiredKeyUsage"/> as its sole key usage, and that its subject kind is permitted
	/// for that usage. The certificate's validity window is <em>not</em> checked here - see
	/// <see cref="EnsureValidAt"/>.
	/// <para>
	/// <paramref name="certificateSignatureFile"/> is the content of the issued <c>cert_&lt;id&gt;.sig</c>
	/// file: the signature base64-encoded as text, which is how Macro Deck issues it and how a signed
	/// artifact carries it.
	/// </para>
	/// </summary>
	public static SigningCertificateVerificationResult Verify(byte[] certificateBytes,
		byte[] certificateSignatureFile,
		ReadOnlySpan<byte> rootPublicKey,
		string requiredKeyUsage)
	{
		ArgumentNullException.ThrowIfNull(certificateBytes);
		ArgumentNullException.ThrowIfNull(certificateSignatureFile);
		ArgumentException.ThrowIfNullOrWhiteSpace(requiredKeyUsage);

		var rootKey = Ed25519VerificationKey.TryImport(rootPublicKey) ??
			throw new ArgumentException($"The root public key must be exactly {Ed25519KeyPair.PublicKeyLength} bytes.",
				nameof(rootPublicKey));

		var certificateSignature = DecodeSignatureFile(certificateSignatureFile);
		if (certificateSignature is null || !rootKey.Verify(certificateBytes, certificateSignature))
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateUntrusted,
				"The certificate signature does not verify against the root key.");
		}

		SigningCertificate? certificate;
		try
		{
			certificate = JsonSerializer.Deserialize<SigningCertificate>(certificateBytes, SigningJson.Options);
		}
		catch (JsonException ex)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed, ex.Message);
		}

		if (certificate is null)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed,
				"The certificate document is empty.");
		}

		if (certificate.SchemaVersion != 1 ||
			certificate.Algorithm != "ed25519" ||
			string.IsNullOrWhiteSpace(certificate.CertificateId))
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed,
				"The certificate has an unsupported shape or algorithm.");
		}

		if (certificate.KeyUsage.Count != 1 || certificate.KeyUsage[0] != requiredKeyUsage)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateWrongPurpose,
				$"The certificate does not exclusively permit '{requiredKeyUsage}' signing.");
		}

		var validSubject = requiredKeyUsage == RegistryKeyUsage
			? certificate.Subject.Kind == "service"
			: certificate.Subject.Kind is "creator" or "organization";
		if (!validSubject)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateWrongPurpose,
				$"The certificate subject is not permitted for '{requiredKeyUsage}' signing.");
		}

		var certificatePublicBytes = Base64Material.TryDecode(certificate.PublicKey, Ed25519KeyPair.PublicKeyLength);
		if (certificatePublicBytes is null)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed,
				"The certificate's public key is not a valid base64-encoded Ed25519 key.");
		}

		var certificatePublicKey = Ed25519VerificationKey.TryImport(certificatePublicBytes) ??
			throw new InvalidOperationException("An already-length-checked key failed to import.");

		return SigningCertificateVerificationResult.Ok(new TrustedSigningCertificate(certificate,
			certificatePublicKey));
	}

	/// <summary>Checks whether <paramref name="certificate"/>'s validity window covers <paramref name="at"/>.
	/// A signature's validity is always evaluated at the instant it records having been made, not at the
	/// instant verification happens - a package signed while its certificate was valid stays verifiable
	/// after the certificate expires.</summary>
	public static SigningFailure? EnsureValidAt(SigningCertificate certificate, DateTimeOffset at)
	{
		ArgumentNullException.ThrowIfNull(certificate);

		if (at < certificate.NotBefore)
		{
			return new SigningFailure(SigningError.CertificateNotYetValid,
				$"The certificate is not valid until {certificate.NotBefore:O}.");
		}

		if (at > certificate.NotAfter)
		{
			return new SigningFailure(SigningError.CertificateExpired,
				$"The certificate expired at {certificate.NotAfter:O}.");
		}

		return null;
	}

	private static byte[]? DecodeSignatureFile(byte[] certificateSignatureFile) =>
		Base64Material.TryDecode(Encoding.UTF8.GetString(certificateSignatureFile),
			Ed25519KeyPair.SignatureLength);
}

/// <summary>The outcome of <see cref="SigningCertificateChain.Verify(byte[], byte[], string)"/>.</summary>
public sealed record SigningCertificateVerificationResult
{
	public required bool Success { get; init; }

	public TrustedSigningCertificate? TrustedCertificate { get; init; }

	public SigningError? Error { get; init; }

	public string? Message { get; init; }

	public static SigningCertificateVerificationResult Ok(TrustedSigningCertificate trustedCertificate) =>
		new() { Success = true, TrustedCertificate = trustedCertificate };

	public static SigningCertificateVerificationResult Fail(SigningError error, string message) =>
		new() { Success = false, Error = error, Message = message };
}
