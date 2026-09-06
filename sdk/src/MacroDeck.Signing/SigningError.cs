namespace MacroDeck.Signing;

/// <summary>Why a signing or verification operation could not complete.</summary>
public enum SigningError
{
	/// <summary>The certificate document could not be read from disk or from the supplied bytes.</summary>
	CertificateUnreadable,

	/// <summary>The certificate document is not valid JSON, has an unsupported <c>schemaVersion</c>, or is
	/// missing a required property.</summary>
	CertificateMalformed,

	/// <summary>The certificate's bytes do not verify against the root signature supplied alongside it.
	/// </summary>
	CertificateUntrusted,

	/// <summary>The certificate does not exclusively permit the required key usage, or its subject kind is
	/// not permitted for that usage.</summary>
	CertificateWrongPurpose,

	/// <summary>The evaluated instant is before the certificate's <c>notBefore</c>.</summary>
	CertificateNotYetValid,

	/// <summary>The evaluated instant is after the certificate's <c>notAfter</c>.</summary>
	CertificateExpired,

	/// <summary>The private key file could not be read from disk or from the supplied bytes.</summary>
	PrivateKeyUnreadable,

	/// <summary>The private key is not valid base64, or does not decode to a 32-byte raw Ed25519 key.
	/// </summary>
	PrivateKeyMalformed,

	/// <summary>The supplied private key's public key does not match the certificate's public key.</summary>
	PrivateKeyDoesNotMatchCertificate,

	/// <summary>The package could not be opened as a ZIP archive, or could not be read from disk.</summary>
	PackageUnreadable,

	/// <summary>The package's extension does not name a package format this library signs or verifies.
	/// </summary>
	PackageFormatUnsupported,

	/// <summary>The archive contains no root manifest entry (<c>manifest.json</c> or, for an icon pack,
	/// <c>pack.json</c>).</summary>
	ManifestMissing,

	/// <summary>The manifest could not be parsed as JSON, is missing a property this library requires, or -
	/// for a plugin - failed <see cref="MacroDeck.Plugin.Packaging.Manifest.IPluginManifestReader"/>
	/// validation.</summary>
	ManifestMalformed,

	/// <summary>The manifest exceeds the configured size limit.</summary>
	ManifestTooLarge,

	/// <summary>A declared file's SHA-256 does not match the archive entry's actual content.</summary>
	FileDigestMismatch,

	/// <summary>A declared file's size does not match the archive entry's actual length.</summary>
	FileSizeMismatch,

	/// <summary>The archive contains an entry that is neither the declared manifest, an entry named in
	/// <c>files[]</c>, nor signature material at the archive root.</summary>
	UndeclaredFile,

	/// <summary>A file named in <c>files[]</c> has no corresponding archive entry.</summary>
	DeclaredFileMissing,

	/// <summary>An archive entry's path is unsafe: absolute, path-traversing, or otherwise rejected by
	/// <see cref="MacroDeck.Plugin.Packaging.Artifacts.PluginArtifactEntryPolicy"/>.</summary>
	UnsafeEntry,

	/// <summary>The manifest already carries a <c>signature</c> object.</summary>
	AlreadySigned,

	/// <summary>The manifest carries no <c>signature</c> object.</summary>
	SignatureMissing,

	/// <summary>The signature is missing a required property, has an unsupported <c>schemaVersion</c>
	/// (registry signatures only), or its <c>value</c> is not a well-formed base64 Ed25519 signature.
	/// </summary>
	SignatureMalformed,

	/// <summary>The signature's <c>keyId</c> does not equal the supplied certificate's <c>certificateId</c>.
	/// </summary>
	SignatureKeyIdMismatch,

	/// <summary>The signature declares an algorithm other than <c>ed25519</c>.</summary>
	SignatureAlgorithmUnsupported,

	/// <summary>The Ed25519 signature does not verify against the signed bytes.</summary>
	SignatureInvalid,

	/// <summary>A signature this library just produced failed to verify against its own certificate. This
	/// never happens in practice; it exists so a broken key pair fails loudly instead of shipping an
	/// artifact that looks signed but is not verifiable.</summary>
	SelfVerificationFailed,

	/// <summary>The output path already exists. Signing never overwrites an existing file.</summary>
	OutputExists,

	/// <summary>Writing the output failed, e.g. due to an I/O error.</summary>
	WriteFailed
}

/// <summary>A failed signing or verification operation.</summary>
public sealed record SigningFailure(SigningError Error, string Message);
