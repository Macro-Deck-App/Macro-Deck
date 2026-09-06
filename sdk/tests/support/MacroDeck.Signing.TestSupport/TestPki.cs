using System.Text;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using NSec.Cryptography;

namespace MacroDeck.Signing.TestSupport;

/// <summary>
/// The only place this repository ever mints a certificate. A real Macro Deck root key exists only in the
/// offline key-generation tool outside this repository, so every test here signs against a throwaway root
/// of its own - never <see cref="MacroDeckRootKey" /> - and passes that root's public key explicitly to
/// <see cref="SigningCertificateChain.Verify(byte[], byte[], ReadOnlySpan{byte}, string)" />.
/// </summary>
public static class TestPki
{
	/// <summary>The root every test treats as "the trusted root" unless it is deliberately testing a
	/// mismatch.</summary>
	public static readonly (byte[] PrivateKey, byte[] PublicKey) Root = Ed25519KeyPair.Create();

	/// <summary>A second, unrelated root - for "signed by a different root than the one being trusted".
	/// </summary>
	public static readonly (byte[] PrivateKey, byte[] PublicKey) OtherRoot = Ed25519KeyPair.Create();

	public sealed record IssuedCertificate(
		byte[] CertificateBytes,
		byte[] CertificateSignatureBytes,
		byte[] PrivateKey,
		byte[] PublicKey,
		string CertificateId);

	/// <summary>
	/// Issues a certificate signed by <paramref name="signingRoot" /> (<see cref="Root" /> by default).
	/// Every parameter defaults to a value that passes <see cref="SigningCertificateChain.Verify(byte[], byte[], ReadOnlySpan{byte}, string)" />
	/// for <c>package</c> signing, so a test only needs to override the one thing it is exercising.
	/// </summary>
	public static IssuedCertificate IssueCertificate(
		string subjectKind = "creator",
		IReadOnlyList<string>? keyUsage = null,
		DateTimeOffset? notBefore = null,
		DateTimeOffset? notAfter = null,
		(byte[] PrivateKey, byte[] PublicKey)? signingRoot = null,
		string? rootKeyId = null,
		int schemaVersion = 1,
		string algorithm = "ed25519",
		(byte[] PrivateKey, byte[] PublicKey)? subjectKeyOverride = null)
	{
		var subjectKeys = subjectKeyOverride ?? Ed25519KeyPair.Create();
		var root = signingRoot ?? Root;
		var certificateId = "cert_" + Guid.NewGuid().ToString("N");

		var certificate = new SigningCertificate
		{
			Schema = "https://schemas.macro-deck.app/macrodeck-certificate-v1.schema.json",
			SchemaVersion = schemaVersion,
			Subject = new SigningCertificateSubject { Kind = subjectKind, Id = "creator-1", Name = "Test Creator" },
			Algorithm = algorithm,
			CertificateId = certificateId,
			PublicKey = Convert.ToBase64String(subjectKeys.PublicKey),
			KeyUsage = keyUsage ?? [SigningCertificateChain.PackageKeyUsage],
			NotBefore = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
			NotAfter = notAfter ?? DateTimeOffset.UtcNow.AddYears(1),
			IssuedAt = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
			RootKeyId = rootKeyId ?? "root_test0001"
		};

		var certificateBytes = SigningJson.Serialize(certificate);
		var certificateSignatureBytes = SignCertificateBytesAsBase64Text(root.PrivateKey, certificateBytes);

		return new IssuedCertificate(certificateBytes,
			certificateSignatureBytes,
			subjectKeys.PrivateKey,
			subjectKeys.PublicKey,
			certificateId);
	}

	/// <summary>Runs the issued certificate through <see cref="SigningCertificateChain.Verify(byte[], byte[], ReadOnlySpan{byte}, string)" />
	/// against <paramref name="trustedRoot" /> (<see cref="Root" /> by default) and unwraps a trusted result -
	/// convenience for the many tests that only care about the certificate once it has already passed the
	/// chain check.</summary>
	public static TrustedSigningCertificate VerifyChain(IssuedCertificate issued,
		string requiredKeyUsage,
		(byte[] PrivateKey, byte[] PublicKey)? trustedRoot = null)
	{
		var root = trustedRoot ?? Root;
		var result = SigningCertificateChain.Verify(issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			root.PublicKey,
			requiredKeyUsage);
		if (!result.Success)
		{
			throw new InvalidOperationException(
				$"Expected the issued test certificate to verify, but it failed with {result.Error}: {result.Message}");
		}

		return result.TrustedCertificate!;
	}

	/// <summary>Re-serializes <paramref name="certificateBytes" /> with different indentation and property
	/// order - paired with the original <c>.sig</c> by the caller, the root signature then no longer covers
	/// the bytes actually presented, exactly the substitution <see cref="SigningCertificateChain" /> has to
	/// reject.</summary>
	public static byte[] ReserializeWithDifferentFormatting(byte[] certificateBytes)
	{
		var node = System.Text.Json.Nodes.JsonNode.Parse(certificateBytes)!.AsObject();
		var reordered = new System.Text.Json.Nodes.JsonObject();
		foreach (var key in node.Select(pair => pair.Key).OrderByDescending(key => key, StringComparer.Ordinal))
		{
			reordered[key] = node[key]?.DeepClone();
		}

		// Different whitespace than SigningJson.Options (no indentation) - still valid JSON, still the same
		// logical document, but not the same bytes.
		return Encoding.UTF8.GetBytes(reordered.ToJsonString() + "\n");
	}

	/// <summary>Signs <paramref name="payload" /> with a raw root private key not owned by any
	/// <see cref="MacroDeck.Signing.Keys.SigningMaterial" /> - only the offline root ever does this, so this
	/// helper reaches past the SDK's own API the same way that tool has to. The certificate's detached
	/// <c>.sig</c> carries the signature base64-encoded as text, matching how Macro Deck issues it. Public so
	/// a test can re-sign a certificate document it deliberately mutated, isolating "the document's shape is
	/// wrong" from "the bytes do not match the signature".</summary>
	public static byte[] SignCertificateBytesAsBase64Text(byte[] rawRootPrivateKey, byte[] payload)
	{
		using var key = Key.Import(SignatureAlgorithm.Ed25519,
			rawRootPrivateKey,
			KeyBlobFormat.RawPrivateKey);
		var signature = SignatureAlgorithm.Ed25519.Sign(key, payload);
		return Encoding.UTF8.GetBytes(Convert.ToBase64String(signature));
	}
}
