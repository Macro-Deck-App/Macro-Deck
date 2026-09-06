namespace MacroDeck.Plugin.Cli.Building;

/// <summary>The diagnostic code for every <see cref="PluginBuildFailureReason" /> - mirrors
/// <see cref="PluginBuildFailureExitCode" />, total over the same enum for the same reason.</summary>
internal static class PluginBuildFailureCode
{
	public static string For(PluginBuildFailureReason? reason) => reason switch
	{
		PluginBuildFailureReason.SourceNotFound => "source-not-found",
		PluginBuildFailureReason.ManifestNotFound => "manifest-not-found",
		PluginBuildFailureReason.ManifestMalformed => "manifest-malformed",
		PluginBuildFailureReason.ManifestInvalid => "manifest-invalid",
		PluginBuildFailureReason.NoEntrypointsDeclared => "no-entrypoints-declared",
		PluginBuildFailureReason.OutputExists => "output-exists",
		PluginBuildFailureReason.BuildConfigNotFound => "build-config-not-found",
		PluginBuildFailureReason.BuildConfigMalformed => "build-config-malformed",
		PluginBuildFailureReason.BuildConfigInvalid => "build-config-invalid",
		PluginBuildFailureReason.RidNotDeclared => "rid-not-declared",
		PluginBuildFailureReason.TargetNotConfigured => "target-not-configured",
		PluginBuildFailureReason.EntrypointLayoutInvalid => "entrypoint-layout-invalid",
		PluginBuildFailureReason.BuildToolNotFound => "build-tool-not-found",
		PluginBuildFailureReason.BuildFailed => "build-failed",
		PluginBuildFailureReason.TargetOutputMissing => "target-output-missing",
		PluginBuildFailureReason.EntrypointMissing => "entrypoint-missing",
		PluginBuildFailureReason.StagingFailed => "staging-failed",
		null => "build-failed",
		_ => "build-failed"
	};
}
