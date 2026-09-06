namespace MacroDeck.Plugin.Cli.Scaffolding;

internal static class PluginScaffoldFailureCode
{
	public static string For(PluginScaffoldFailureReason? reason) => reason switch
	{
		PluginScaffoldFailureReason.DotnetNotFound => "dotnet-not-found",
		PluginScaffoldFailureReason.TemplateInstallFailed => "template-install-failed",
		PluginScaffoldFailureReason.TemplateCreateFailed => "template-create-failed",
		PluginScaffoldFailureReason.ManifestNotGenerated => "manifest-not-generated",
		PluginScaffoldFailureReason.ManifestRewriteFailed => "manifest-rewrite-failed",
		PluginScaffoldFailureReason.WriteFailed => "write-failed",
		null => "scaffold-failed",
		_ => "scaffold-failed"
	};
}
