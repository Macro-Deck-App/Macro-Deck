namespace MacroDeck.Plugin.Cli.Scaffolding;

internal static class PluginScaffoldFailureExitCode
{
	public static int For(PluginScaffoldFailureReason? reason) => reason switch
	{
		PluginScaffoldFailureReason.DotnetNotFound => ExitCode.InputUnreadable,
		PluginScaffoldFailureReason.TemplateInstallFailed => ExitCode.InputUnreadable,
		PluginScaffoldFailureReason.TemplateCreateFailed => ExitCode.SubjectInvalid,
		PluginScaffoldFailureReason.ManifestNotGenerated => ExitCode.InternalError,
		PluginScaffoldFailureReason.ManifestRewriteFailed => ExitCode.InternalError,
		PluginScaffoldFailureReason.WriteFailed => ExitCode.InternalError,
		null => ExitCode.InternalError,
		_ => ExitCode.InternalError
	};
}
