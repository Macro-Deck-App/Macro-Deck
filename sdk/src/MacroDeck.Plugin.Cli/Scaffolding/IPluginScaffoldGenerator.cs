namespace MacroDeck.Plugin.Cli.Scaffolding;

internal enum PluginTemplateStatus
{
	NotInstalled,
	Outdated,
	UpToDate
}

internal readonly record struct PluginTemplateOperationResult(bool Success, string? Output);

/// <summary>
/// The seam between <c>new</c> and the real <c>dotnet new</c> template toolchain. Everything upstream of
/// this interface - manifest post-processing, build config generation, cleanup on failure - is exercised
/// by tests entirely through a fake implementation: no test may install a template, touch the network, or
/// invoke the real <c>dotnet new</c>, which would mutate the machine-global template store.
/// </summary>
internal interface IPluginScaffoldGenerator
{
	Task<PluginTemplateStatus> ProbeAsync(CancellationToken cancellationToken);

	Task<PluginTemplateOperationResult> InstallAsync(string? templateVersion, CancellationToken cancellationToken);

	Task<PluginTemplateOperationResult> UpdateAsync(CancellationToken cancellationToken);

	Task<PluginTemplateOperationResult> CreateAsync(PluginScaffoldRequest request, CancellationToken cancellationToken);
}
