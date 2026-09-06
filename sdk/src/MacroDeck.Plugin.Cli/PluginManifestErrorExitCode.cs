using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli;

/// <summary>
/// The total mapping from <see cref="PluginManifestError" /> to an <see cref="ExitCode" />, used wherever
/// <see cref="IPluginManifestReader" /> reads a bare <c>manifest.json</c> directly (not through an
/// artifact). See <see cref="PluginInstallErrorExitCode" /> for the artifact-reader counterpart and why
/// this has an explicit arm for every named member plus a discard arm C# requires but this enum should
/// never actually hit.
/// </summary>
internal static class PluginManifestErrorExitCode
{
	public static int For(PluginManifestError error) => error switch
	{
		// No manifest.json at the given path - could not be read at all.
		PluginManifestError.NotFound => ExitCode.InputUnreadable,

		// Every other case is a manifest that was read successfully and found wanting.
		PluginManifestError.Malformed => ExitCode.SubjectInvalid,
		PluginManifestError.UnsupportedManifestVersion => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidPluginId => ExitCode.SubjectInvalid,
		PluginManifestError.IdMismatch => ExitCode.SubjectInvalid,
		PluginManifestError.VersionMismatch => ExitCode.SubjectInvalid,
		PluginManifestError.NoEntrypoints => ExitCode.SubjectInvalid,
		PluginManifestError.EntrypointOutsideVersionDirectory => ExitCode.SubjectInvalid,
		PluginManifestError.EntrypointMissing => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidSettings => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidCompatibility => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidDependency => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidEntrypointRuntime => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidSignature => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidFileDigest => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidPermission => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidPublisher => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidName => ExitCode.SubjectInvalid,
		PluginManifestError.InvalidLanguage => ExitCode.SubjectInvalid,

		_ => ExitCode.InternalError
	};
}
