using System.Text;
using System.Text.Json;
using MacroDeck.Signing.Keys;

namespace MacroDeck.Signing.Certificates;

/// <summary>
/// Verifies a <see cref="SigningCertificate"/> document against the Macro Deck root key, either directly or
/// through exactly one issuer certificate the root signed. Verification is always over the certificates' exact
/// file bytes, never a re-serialized document - re-serializing, even just reformatting whitespace, invalidates
/// the signature. Revocation is never consulted here.
/// </summary>
public static class SigningCertificateChain
{
	/// <summary>The key usage a creator or organization package-signing certificate must exclusively carry.
	/// </summary>
	public const string PackageKeyUsage = "package";

	/// <summary>The key usage Macro Deck's own registry-signing certificate must exclusively carry.
	/// </summary>
	public const string RegistryKeyUsage = "registry";

	/// <summary>The key usage an issuer certificate must exclusively carry. An issuer certificate is signed by
	/// the root, has the subject kind <c>"issuer"</c>, and signs package and registry certificates, never a
	/// package or a registry manifest.</summary>
	public const string IssuerKeyUsage = "issuer";

	private const string IssuerSubjectKind = "issuer";

	private const string IssuerPropertyName = "issuer";

	/// <summary>Verifies a root-signed <paramref name="certificateBytes"/> against <see cref="MacroDeckRootKey"/>.
	/// A certificate that names an issuer fails with <see cref="SigningError.CertificateIssuerMissing"/>; use
	/// <see cref="Verify(byte[], byte[], byte[], byte[], string)"/> for it.</summary>
	public static SigningCertificateVerificationResult Verify(byte[] certificateBytes,
		byte[] certificateSignatureFile,
		string requiredKeyUsage)
	{
		return Verify(certificateBytes, certificateSignatureFile, MacroDeckRootKey.PublicKey, requiredKeyUsage);
	}

	/// <summary>
	/// Verifies a root-signed <paramref name="certificateBytes"/> against <paramref name="rootPublicKey"/>: that
	/// <paramref name="certificateSignatureFile"/> is a valid Ed25519 signature over the certificate's exact
	/// bytes, that the document has the expected shape and algorithm, that it carries exactly
	/// <paramref name="requiredKeyUsage"/> as its sole key usage, and that its subject kind is permitted
	/// for that usage. The certificate's validity window is <em>not</em> checked here - see
	/// <see cref="EnsureValidAt(TrustedSigningCertificate, DateTimeOffset)"/>. A certificate that names an issuer
	/// fails with <see cref="SigningError.CertificateIssuerMissing"/>.
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
		return Verify(certificateBytes, certificateSignatureFile, null, null, rootPublicKey, requiredKeyUsage);
	}

	/// <summary>Verifies <paramref name="certificateBytes"/> against <see cref="MacroDeckRootKey"/>, through
	/// the issuer certificate when one is supplied. See
	/// <see cref="Verify(byte[], byte[], byte[], byte[], ReadOnlySpan{byte}, string)"/>.</summary>
	public static SigningCertificateVerificationResult Verify(byte[] certificateBytes,
		byte[] certificateSignatureFile,
		byte[]? issuerCertificateBytes,
		byte[]? issuerCertificateSignatureFile,
		string requiredKeyUsage)
	{
		return Verify(certificateBytes,
			certificateSignatureFile,
			issuerCertificateBytes,
			issuerCertificateSignatureFile,
			MacroDeckRootKey.PublicKey,
			requiredKeyUsage);
	}

	/// <summary>
	/// Verifies <paramref name="certificateBytes"/> against <paramref name="rootPublicKey"/>, directly or through
	/// one issuer certificate.
	/// <para>
	/// Without issuer material the certificate must be signed by the root, exactly as in
	/// <see cref="Verify(byte[], byte[], ReadOnlySpan{byte}, string)"/>. With it, the issuer certificate must be
	/// signed by the root, carry exactly <see cref="IssuerKeyUsage"/> and name no issuer itself; the certificate
	/// must be signed by the issuer's key, name the issuer's <c>certificateId</c> and <c>rootKeyId</c>, carry
	/// exactly <paramref name="requiredKeyUsage"/>, and have a validity window inside the issuer's.
	/// </para>
	/// <para>
	/// Issuer material for a certificate that names no issuer fails with
	/// <see cref="SigningError.CertificateIssuerMismatch"/>; a certificate that names an issuer without issuer
	/// material fails with <see cref="SigningError.CertificateIssuerMissing"/>. Validity windows are not
	/// evaluated here - see <see cref="EnsureValidAt(TrustedSigningCertificate, DateTimeOffset)"/>.
	/// </para>
	/// </summary>
	public static SigningCertificateVerificationResult Verify(byte[] certificateBytes,
		byte[] certificateSignatureFile,
		byte[]? issuerCertificateBytes,
		byte[]? issuerCertificateSignatureFile,
		ReadOnlySpan<byte> rootPublicKey,
		string requiredKeyUsage)
	{
		ArgumentNullException.ThrowIfNull(certificateBytes);
		ArgumentNullException.ThrowIfNull(certificateSignatureFile);
		ArgumentException.ThrowIfNullOrWhiteSpace(requiredKeyUsage);

		var rootKey = Ed25519VerificationKey.TryImport(rootPublicKey) ??
			throw new ArgumentException($"The root public key must be exactly {Ed25519KeyPair.PublicKeyLength} bytes.",
				nameof(rootPublicKey));

		if (issuerCertificateBytes is null != issuerCertificateSignatureFile is null)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateIssuerMissing,
				"The issuer certificate and its signature must be supplied together.");
		}

		// Read before any signature check only to choose which key the certificate must verify against; every
		// property the result relies on comes from the verified document.
		var namesIssuer = DeclaresIssuer(certificateBytes);

		if (requiredKeyUsage == IssuerKeyUsage && (namesIssuer || issuerCertificateBytes is not null))
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateWrongPurpose,
				"An issuer certificate must be signed directly by the root.");
		}

		if (issuerCertificateBytes is null)
		{
			return namesIssuer
				? SigningCertificateVerificationResult.Fail(SigningError.CertificateIssuerMissing,
					"The certificate names an issuer certificate, but none was supplied.")
				: VerifySigned(certificateBytes, certificateSignatureFile, rootKey, requiredKeyUsage, null);
		}

		if (!namesIssuer)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateIssuerMismatch,
				"An issuer certificate was supplied for a certificate the root signed directly.");
		}

		var issuerResult = VerifySigned(issuerCertificateBytes,
			issuerCertificateSignatureFile!,
			rootKey,
			IssuerKeyUsage,
			null);
		if (!issuerResult.Success)
		{
			return SigningCertificateVerificationResult.Fail(issuerResult.Error!.Value,
				$"The issuer certificate is not trusted: {issuerResult.Message}");
		}

		var issuer = issuerResult.TrustedCertificate!;
		return VerifySigned(certificateBytes,
			certificateSignatureFile,
			issuer.PublicKey,
			requiredKeyUsage,
			issuer.Certificate);
	}

	/// <summary>Checks whether <paramref name="certificate"/>'s validity window covers <paramref name="at"/>.
	/// A signature's validity is always evaluated at the instant it records having been made, not at the
	/// instant verification happens - a package signed while its certificate was valid stays verifiable
	/// after the certificate expires.</summary>
	public static SigningFailure? EnsureValidAt(SigningCertificate certificate, DateTimeOffset at)
	{
		ArgumentNullException.ThrowIfNull(certificate);

		return EnsureWindow(certificate, at, "certificate");
	}

	/// <summary>Checks whether the validity windows of <paramref name="trusted"/>'s issuer certificate, when it
	/// has one, and of the certificate itself both cover <paramref name="at"/>, evaluated the same way as
	/// <see cref="EnsureValidAt(SigningCertificate, DateTimeOffset)"/>.</summary>
	public static SigningFailure? EnsureValidAt(TrustedSigningCertificate trusted, DateTimeOffset at)
	{
		ArgumentNullException.ThrowIfNull(trusted);

		return (trusted.Issuer is { } issuer ? EnsureWindow(issuer, at, "issuer certificate") : null) ??
			EnsureWindow(trusted.Certificate, at, "certificate");
	}

	private static SigningFailure? EnsureWindow(SigningCertificate certificate, DateTimeOffset at, string subject)
	{
		if (at < certificate.NotBefore)
		{
			return new SigningFailure(SigningError.CertificateNotYetValid,
				$"The {subject} is not valid until {certificate.NotBefore:O}.");
		}

		if (at > certificate.NotAfter)
		{
			return new SigningFailure(SigningError.CertificateExpired,
				$"The {subject} expired at {certificate.NotAfter:O}.");
		}

		return null;
	}

	private static SigningCertificateVerificationResult VerifySigned(byte[] certificateBytes,
		byte[] certificateSignatureFile,
		Ed25519VerificationKey signerKey,
		string requiredKeyUsage,
		SigningCertificate? issuer)
	{
		var certificateSignature = DecodeSignatureFile(certificateSignatureFile);
		if (certificateSignature is null || !signerKey.Verify(certificateBytes, certificateSignature))
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateUntrusted,
				issuer is null
					? "The certificate signature does not verify against the root key."
					: "The certificate signature does not verify against its issuer certificate.");
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

		if (certificate.SchemaVersion is not (1 or 2) ||
			certificate.Algorithm != "ed25519" ||
			string.IsNullOrWhiteSpace(certificate.CertificateId))
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed,
				"The certificate has an unsupported shape or algorithm.");
		}

		if (certificate.Issuer is not null &&
			(certificate.SchemaVersion != 2 || string.IsNullOrWhiteSpace(certificate.Issuer)))
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed,
				"Only a schema version 2 certificate can name an issuer, and the name must not be empty.");
		}

		if (certificate.KeyUsage.Count != 1 || certificate.KeyUsage[0] != requiredKeyUsage)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateWrongPurpose,
				$"The certificate does not exclusively permit '{requiredKeyUsage}' signing.");
		}

		var validSubject = requiredKeyUsage switch
		{
			RegistryKeyUsage => certificate.Subject.Kind == "service",
			IssuerKeyUsage => certificate.Subject.Kind == IssuerSubjectKind,
			_ => certificate.Subject.Kind is "creator" or "organization"
		};
		if (!validSubject)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateWrongPurpose,
				$"The certificate subject is not permitted for '{requiredKeyUsage}' signing.");
		}

		if (requiredKeyUsage == IssuerKeyUsage && certificate.SchemaVersion != 2)
		{
			return SigningCertificateVerificationResult.Fail(SigningError.CertificateMalformed,
				"An issuer certificate must use schema version 2.");
		}

		if (issuer is null && certificate.Issuer is not null)
		{
			return requiredKeyUsage == IssuerKeyUsage
				? SigningCertificateVerificationResult.Fail(SigningError.CertificateWrongPurpose,
					"An issuer certificate must be signed directly by the root.")
				: SigningCertificateVerificationResult.Fail(SigningError.CertificateIssuerMissing,
					"The certificate names an issuer certificate, but none was supplied.");
		}

		if (issuer is not null)
		{
			if (!string.Equals(certificate.Issuer, issuer.CertificateId, StringComparison.Ordinal) ||
				!string.Equals(certificate.RootKeyId, issuer.RootKeyId, StringComparison.Ordinal))
			{
				return SigningCertificateVerificationResult.Fail(SigningError.CertificateIssuerMismatch,
					"The certificate does not name the supplied issuer certificate and its root.");
			}

			if (certificate.NotBefore < issuer.NotBefore || certificate.NotAfter > issuer.NotAfter)
			{
				return SigningCertificateVerificationResult.Fail(SigningError.CertificateOutlivesIssuer,
					"The certificate's validity window is not inside its issuer certificate's.");
			}
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
			certificatePublicKey,
			issuer));
	}

	internal static bool DeclaresIssuer(byte[] certificateBytes)
	{
		try
		{
			using var document = JsonDocument.Parse(certificateBytes);
			return document.RootElement.ValueKind == JsonValueKind.Object &&
				document.RootElement.TryGetProperty(IssuerPropertyName, out var issuer) &&
				issuer.ValueKind != JsonValueKind.Null;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static byte[]? DecodeSignatureFile(byte[] certificateSignatureFile) =>
		Base64Material.TryDecode(Encoding.UTF8.GetString(certificateSignatureFile),
			Ed25519KeyPair.SignatureLength);
}

/// <summary>The outcome of <see cref="SigningCertificateChain.Verify(byte[], byte[], byte[], byte[], ReadOnlySpan{byte}, string)"/>.</summary>
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
