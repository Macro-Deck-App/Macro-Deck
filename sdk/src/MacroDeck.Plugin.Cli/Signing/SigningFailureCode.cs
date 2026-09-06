using MacroDeck.Signing;

namespace MacroDeck.Plugin.Cli.Signing;

/// <summary>The diagnostic code for every <see cref="SigningError" /> - mirrors
/// <see cref="SigningFailureExitCode" />, total over the same enum for the same reason.</summary>
internal static class SigningFailureCode
{
	public static string For(SigningError error) => error switch
	{
		SigningError.CertificateUnreadable => "certificate-unreadable",
		SigningError.CertificateMalformed => "certificate-malformed",
		SigningError.CertificateUntrusted => "certificate-untrusted",
		SigningError.CertificateWrongPurpose => "certificate-wrong-purpose",
		SigningError.CertificateNotYetValid => "certificate-not-yet-valid",
		SigningError.CertificateExpired => "certificate-expired",
		SigningError.PrivateKeyUnreadable => "private-key-unreadable",
		SigningError.PrivateKeyMalformed => "private-key-malformed",
		SigningError.PrivateKeyDoesNotMatchCertificate => "private-key-does-not-match-certificate",
		SigningError.PackageUnreadable => "package-unreadable",
		SigningError.PackageFormatUnsupported => "package-format-unsupported",
		SigningError.ManifestMissing => "manifest-missing",
		SigningError.ManifestMalformed => "manifest-malformed",
		SigningError.ManifestTooLarge => "manifest-too-large",
		SigningError.FileDigestMismatch => "file-digest-mismatch",
		SigningError.FileSizeMismatch => "file-size-mismatch",
		SigningError.UndeclaredFile => "undeclared-file",
		SigningError.DeclaredFileMissing => "declared-file-missing",
		SigningError.UnsafeEntry => "unsafe-entry",
		SigningError.AlreadySigned => "already-signed",
		SigningError.SignatureMissing => "signature-missing",
		SigningError.SignatureMalformed => "signature-malformed",
		SigningError.SignatureKeyIdMismatch => "signature-key-id-mismatch",
		SigningError.SignatureAlgorithmUnsupported => "signature-algorithm-unsupported",
		SigningError.SignatureInvalid => "signature-invalid",
		SigningError.SelfVerificationFailed => "self-verification-failed",
		SigningError.OutputExists => "output-exists",
		SigningError.WriteFailed => "write-failed",
		_ => "signing-failed"
	};
}
