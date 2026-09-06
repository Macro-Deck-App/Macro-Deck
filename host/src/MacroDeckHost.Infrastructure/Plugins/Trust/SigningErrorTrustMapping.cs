using MacroDeck.Signing;
using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

internal static class SigningErrorTrustMapping
{
	public static PluginTrustVerdict ToVerdict(this SigningError error) => error switch
	{
		SigningError.SignatureMissing => PluginTrustVerdict.Unsigned,

		SigningError.SignatureInvalid => PluginTrustVerdict.SignatureInvalid,
		SigningError.SignatureKeyIdMismatch => PluginTrustVerdict.SignatureInvalid,

		SigningError.SignatureMalformed => PluginTrustVerdict.Malformed,
		SigningError.CertificateMalformed => PluginTrustVerdict.Malformed,
		SigningError.CertificateUnreadable => PluginTrustVerdict.Malformed,
		SigningError.ManifestMissing => PluginTrustVerdict.Malformed,
		SigningError.ManifestMalformed => PluginTrustVerdict.Malformed,
		SigningError.ManifestTooLarge => PluginTrustVerdict.Malformed,
		SigningError.PackageFormatUnsupported => PluginTrustVerdict.Malformed,

		SigningError.CertificateUntrusted => PluginTrustVerdict.UntrustedRoot,

		SigningError.CertificateWrongPurpose => PluginTrustVerdict.WrongCertificatePurpose,

		SigningError.CertificateNotYetValid => PluginTrustVerdict.CertificateNotValidAtSignature,
		SigningError.CertificateExpired => PluginTrustVerdict.CertificateNotValidAtSignature,

		SigningError.FileDigestMismatch => PluginTrustVerdict.ContentMismatch,
		SigningError.FileSizeMismatch => PluginTrustVerdict.ContentMismatch,
		SigningError.UndeclaredFile => PluginTrustVerdict.ContentMismatch,
		SigningError.DeclaredFileMissing => PluginTrustVerdict.ContentMismatch,
		SigningError.UnsafeEntry => PluginTrustVerdict.ContentMismatch,

		SigningError.PackageUnreadable => PluginTrustVerdict.VerificationUnavailable,
		SigningError.SignatureAlgorithmUnsupported => PluginTrustVerdict.VerificationUnavailable,
		SigningError.PrivateKeyUnreadable => PluginTrustVerdict.VerificationUnavailable,
		SigningError.PrivateKeyMalformed => PluginTrustVerdict.VerificationUnavailable,
		SigningError.PrivateKeyDoesNotMatchCertificate => PluginTrustVerdict.VerificationUnavailable,
		SigningError.AlreadySigned => PluginTrustVerdict.VerificationUnavailable,
		SigningError.SelfVerificationFailed => PluginTrustVerdict.VerificationUnavailable,
		SigningError.OutputExists => PluginTrustVerdict.VerificationUnavailable,
		SigningError.WriteFailed => PluginTrustVerdict.VerificationUnavailable,

		_ => PluginTrustVerdict.VerificationUnavailable
	};
}
