using MacroDeck.Plugin.Cli.Processes;

namespace MacroDeck.Plugin.Cli.Building;

/// <summary>The real <see cref="IPluginBuildRunner" />, launching the configured tool directly - no shell,
/// so nothing in a build recipe is ever evaluated as a command line.</summary>
internal sealed class ProcessPluginBuildRunner : IPluginBuildRunner
{
	public async Task<PluginBuildRunResult> RunAsync(string executable,
		IReadOnlyList<string> arguments,
		string workingDirectory,
		CancellationToken cancellationToken)
	{
		var result = await ProcessRun.RunAsync(executable, arguments, cancellationToken, workingDirectory)
			.ConfigureAwait(false);

		return new PluginBuildRunResult(result.ExitCode, result.StandardOutput, result.StandardError);
	}
}
