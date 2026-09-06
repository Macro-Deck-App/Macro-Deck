namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>The real <see cref="IPluginScaffoldGenerator" />, invoking the actual <c>dotnet new</c>
/// toolchain. Never version-compares an installed template against <paramref name="templateVersion" /> -
/// see <see cref="DotnetTemplateInstaller" /> - so this never reports <see cref="PluginTemplateStatus.Outdated" />;
/// only a fake generator does, for <c>PluginScaffolderTests</c>.</summary>
internal sealed class DotnetNewGenerator : IPluginScaffoldGenerator
{
	public async Task<PluginTemplateStatus> ProbeAsync(CancellationToken cancellationToken)
	{
		var installed = await DotnetTemplateInstaller.IsInstalledAsync(cancellationToken).ConfigureAwait(false);
		return installed ? PluginTemplateStatus.UpToDate : PluginTemplateStatus.NotInstalled;
	}

	public async Task<PluginTemplateOperationResult> InstallAsync(string? templateVersion,
		CancellationToken cancellationToken)
	{
		var result = await DotnetTemplateInstaller.InstallAsync(templateVersion, cancellationToken)
			.ConfigureAwait(false);

		// Re-probed rather than trusted: 'dotnet new install's own exit code and "already installed" wording
		// vary across SDK feature bands.
		var installed = await DotnetTemplateInstaller.IsInstalledAsync(cancellationToken).ConfigureAwait(false);
		return new PluginTemplateOperationResult(installed, result.CombinedOutput);
	}

	public Task<PluginTemplateOperationResult> UpdateAsync(CancellationToken cancellationToken)
		=> InstallAsync(null, cancellationToken);

	public async Task<PluginTemplateOperationResult> CreateAsync(PluginScaffoldRequest request,
		CancellationToken cancellationToken)
	{
		List<string> arguments =
		[
			"new",
			PluginScaffoldDefaults.TemplateShortName,
			"-n",
			request.ProjectName,
			"-o",
			request.Output,
			"--pluginId",
			request.Id,
			"--pluginName",
			request.Name
		];

		if (request.NoRestore)
		{
			arguments.Add("--skipRestore");
		}

		var result = await DotnetProcess.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
		return new PluginTemplateOperationResult(result.Success, result.CombinedOutput);
	}
}
