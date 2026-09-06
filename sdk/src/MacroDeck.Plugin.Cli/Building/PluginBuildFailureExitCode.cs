namespace MacroDeck.Plugin.Cli.Building;

/// <summary>
/// The exit code for every <see cref="PluginBuildFailureReason" />. A file that is missing or could not be
/// launched is an environment problem (<see cref="ExitCode.InputUnreadable" />); a document that was read
/// and found wanting is a defect in the plugin (<see cref="ExitCode.SubjectInvalid" />) - the same split
/// <see cref="PluginManifestErrorExitCode" /> already makes, so <c>build</c> and <c>validate</c> never
/// return different codes for the same bad file.
/// </summary>
internal static class PluginBuildFailureExitCode
{
	public static int For(PluginBuildFailureReason? reason) => reason switch
	{
		PluginBuildFailureReason.SourceNotFound => ExitCode.InputUnreadable,
		PluginBuildFailureReason.ManifestNotFound => ExitCode.InputUnreadable,
		PluginBuildFailureReason.ManifestMalformed => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.ManifestInvalid => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.NoEntrypointsDeclared => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.OutputExists => ExitCode.UsageError,
		PluginBuildFailureReason.BuildConfigNotFound => ExitCode.InputUnreadable,
		PluginBuildFailureReason.BuildConfigMalformed => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.BuildConfigInvalid => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.RidNotDeclared => ExitCode.UsageError,
		PluginBuildFailureReason.TargetNotConfigured => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.EntrypointLayoutInvalid => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.BuildToolNotFound => ExitCode.InputUnreadable,
		PluginBuildFailureReason.BuildFailed => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.TargetOutputMissing => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.EntrypointMissing => ExitCode.SubjectInvalid,
		PluginBuildFailureReason.StagingFailed => ExitCode.InternalError,
		null => ExitCode.InternalError,
		_ => ExitCode.InternalError
	};
}
