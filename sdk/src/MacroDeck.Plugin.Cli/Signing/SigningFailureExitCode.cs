using MacroDeck.Signing;

namespace MacroDeck.Plugin.Cli.Signing;

/// <summary>The exit code for every <see cref="SigningError" /> - mirrors <see cref="SigningFailureCode" />,
/// total over the same enum for the same reason. A file-level read failure
/// (<see cref="ExitCode.InputUnreadable" />) is kept distinct from a content-level problem
/// (<see cref="ExitCode.SubjectInvalid" />) the same way <c>pack</c>'s own failure mapping keeps
/// <c>SourceNotFound</c> apart from <c>SourceEntryRejected</c> - a caller in CI needs to tell an
/// environment problem from a verdict about the artifact.</summary>
internal static class SigningFailureExitCode
{
	public static int For(SigningError error) => error switch
	{
		SigningError.CertificateUnreadable => ExitCode.InputUnreadable,
		SigningError.CertificateMalformed => ExitCode.SubjectInvalid,
		SigningError.CertificateUntrusted => ExitCode.SubjectInvalid,
		SigningError.CertificateWrongPurpose => ExitCode.SubjectInvalid,
		SigningError.CertificateNotYetValid => ExitCode.SubjectInvalid,
		SigningError.CertificateExpired => ExitCode.SubjectInvalid,
		SigningError.PrivateKeyUnreadable => ExitCode.InputUnreadable,
		SigningError.PrivateKeyMalformed => ExitCode.SubjectInvalid,
		SigningError.PrivateKeyDoesNotMatchCertificate => ExitCode.SubjectInvalid,
		SigningError.PackageUnreadable => ExitCode.InputUnreadable,
		SigningError.PackageFormatUnsupported => ExitCode.UsageError,
		SigningError.ManifestMissing => ExitCode.SubjectInvalid,
		SigningError.ManifestMalformed => ExitCode.SubjectInvalid,
		SigningError.ManifestTooLarge => ExitCode.SubjectInvalid,
		SigningError.FileDigestMismatch => ExitCode.SubjectInvalid,
		SigningError.FileSizeMismatch => ExitCode.SubjectInvalid,
		SigningError.UndeclaredFile => ExitCode.SubjectInvalid,
		SigningError.DeclaredFileMissing => ExitCode.SubjectInvalid,
		SigningError.UnsafeEntry => ExitCode.SubjectInvalid,
		SigningError.AlreadySigned => ExitCode.SubjectInvalid,
		SigningError.SignatureMissing => ExitCode.SubjectInvalid,
		SigningError.SignatureMalformed => ExitCode.SubjectInvalid,
		SigningError.SignatureKeyIdMismatch => ExitCode.SubjectInvalid,
		SigningError.SignatureAlgorithmUnsupported => ExitCode.SubjectInvalid,
		SigningError.SignatureInvalid => ExitCode.SubjectInvalid,
		SigningError.SelfVerificationFailed => ExitCode.InternalError,
		SigningError.OutputExists => ExitCode.UsageError,
		SigningError.WriteFailed => ExitCode.InternalError,
		_ => ExitCode.InternalError
	};
}
