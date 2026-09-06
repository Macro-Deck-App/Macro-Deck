using MacroDeck.Plugin.Cli.Processes;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>Every <c>dotnet</c> invocation <c>new</c> makes, funnelled through one place so a missing SDK
/// always surfaces as <see cref="PluginScaffoldFailureReason.DotnetNotFound" /> rather than as the
/// toolchain-neutral <see cref="ProcessLaunchException" /> the process helper throws.</summary>
internal static class DotnetProcess
{
	public static async Task<ProcessRunResult> RunAsync(IReadOnlyList<string> arguments,
		CancellationToken cancellationToken)
	{
		try
		{
			return await ProcessRun.RunAsync("dotnet", arguments, cancellationToken).ConfigureAwait(false);
		}
		catch (ProcessLaunchException ex)
		{
			throw new PluginScaffoldGeneratorException(PluginScaffoldFailureReason.DotnetNotFound,
				$"Could not launch '{ex.FileName}'. Install the .NET SDK and ensure it is on PATH.");
		}
	}
}
