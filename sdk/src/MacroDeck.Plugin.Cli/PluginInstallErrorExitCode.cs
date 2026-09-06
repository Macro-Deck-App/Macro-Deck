using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli;

/// <summary>
/// The total mapping from <see cref="PluginInstallError" /> to an <see cref="ExitCode" />, used by
/// <c>validate</c>, <c>inspect</c> and <c>pack</c> wherever <see cref="IPluginArtifactReader" /> is the
/// source of the failure.
/// <para>
/// Every named member of <see cref="PluginInstallError" /> has its own explicit arm below - the unit
/// tests iterate <see cref="Enum.GetValues{TEnum}()" /> to prove it. The trailing discard arm exists only
/// because C# always considers a plain enum switch non-exhaustive (its underlying value is not closed to
/// named members - any <see langword="int" /> is a legal cast), never as a place a real, named member of
/// this enum is meant to land; it maps to <see cref="ExitCode.InternalError" />, which is exactly what an
/// unrecognised value is - something this mapping did not anticipate.
/// </para>
/// </summary>
internal static class PluginInstallErrorExitCode
{
	public static int For(PluginInstallError error) => error switch
	{
		// The artifact path could not be opened at all - the CI-usability distinction the exit code table
		// exists for.
		PluginInstallError.ArtifactNotFound => ExitCode.InputUnreadable,
		PluginInstallError.InvalidArchive => ExitCode.InputUnreadable,

		// Every one of these is a real, readable artifact whose content is what is wrong - the subject,
		// not the tool's ability to read it.
		PluginInstallError.ArtifactTooLarge => ExitCode.SubjectInvalid,
		PluginInstallError.UnsafeEntry => ExitCode.SubjectInvalid,
		PluginInstallError.ArtifactLimitExceeded => ExitCode.SubjectInvalid,
		PluginInstallError.ManifestMissing => ExitCode.SubjectInvalid,
		PluginInstallError.ManifestInvalid => ExitCode.SubjectInvalid,
		PluginInstallError.IdMismatch => ExitCode.SubjectInvalid,
		PluginInstallError.Incompatible => ExitCode.SubjectInvalid,
		PluginInstallError.HashMismatch => ExitCode.SubjectInvalid,
		PluginInstallError.SignatureInvalid => ExitCode.SubjectInvalid,

		PluginInstallError.Cancelled => ExitCode.Cancelled,

		// Install-lifecycle-only outcomes: nothing the CLI itself calls (it never installs, activates or
		// uninstalls anything) can produce these. Mapped to InternalError so a future codepath that
		// somehow does surface one is loud about it rather than quietly reported as an ordinary
		// validation failure.
		PluginInstallError.AlreadyInstalled => ExitCode.InternalError,
		PluginInstallError.StagingFailed => ExitCode.InternalError,
		PluginInstallError.ActivationFailed => ExitCode.InternalError,
		PluginInstallError.HealthValidationFailed => ExitCode.InternalError,
		PluginInstallError.DependencyInUse => ExitCode.InternalError,
		PluginInstallError.NotInstalled => ExitCode.InternalError,
		PluginInstallError.Failed => ExitCode.InternalError,
		PluginInstallError.SignatureUntrusted => ExitCode.InternalError,
		PluginInstallError.SignatureRevoked => ExitCode.InternalError,
		PluginInstallError.SignatureUnverifiable => ExitCode.InternalError,
		PluginInstallError.UnsignedNotPermitted => ExitCode.InternalError,
		PluginInstallError.TrustDowngrade => ExitCode.InternalError,

		// Never a named member in practice - see this type's own remarks.
		_ => ExitCode.InternalError
	};
}
