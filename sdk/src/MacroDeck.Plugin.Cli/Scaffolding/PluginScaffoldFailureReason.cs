namespace MacroDeck.Plugin.Cli.Scaffolding;

internal enum PluginScaffoldFailureReason
{
	DotnetNotFound,
	TemplateInstallFailed,
	TemplateCreateFailed,
	ManifestNotGenerated,
	ManifestRewriteFailed,
	WriteFailed
}
