using MacroDeck.Plugin.Cli.Processes;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Probes and installs the plugin project template without ever parsing <c>dotnet new list</c>'s output -
/// a localized, terminal-width-dependent table too brittle to parse reliably.
/// </summary>
internal static class DotnetTemplateInstaller
{
	/// <summary>Exit 0 means installed, and only the exit code is read - <c>dotnet new list</c> answers with
	/// 0 when a template matches and 103 when none does. <c>dotnet new &lt;shortName&gt; --help</c> looks like
	/// the more direct question but cannot be used: it exits 0 even for a template that is not installed,
	/// which would skip the install and fail at creation instead. This probe is what makes the
	/// already-installed case never touch the network.</summary>
	public static async Task<bool> IsInstalledAsync(CancellationToken cancellationToken)
	{
		var result = await DotnetProcess
			.RunAsync(["new", "list", PluginScaffoldDefaults.TemplateShortName], cancellationToken)
			.ConfigureAwait(false);

		return result.Success;
	}

	/// <summary>The <c>@</c> form with the <c>*-*</c> prerelease wildcard is mandatory: only prerelease
	/// versions of the template are published, and without the wildcard NuGet prefers stable and finds
	/// nothing.</summary>
	public static Task<ProcessRunResult> InstallAsync(string? templateVersion, CancellationToken cancellationToken)
	{
		var package = templateVersion is null
			? $"{PluginScaffoldDefaults.TemplatePackageId}@*-*"
			: $"{PluginScaffoldDefaults.TemplatePackageId}@{templateVersion}";

		return DotnetProcess.RunAsync(["new", "install", package], cancellationToken);
	}
}
