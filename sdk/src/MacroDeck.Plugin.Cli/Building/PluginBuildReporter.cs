using MacroDeck.Plugin.Cli.Packing;

namespace MacroDeck.Plugin.Cli.Building;

/// <summary>Renders a <see cref="PluginBuildResult" /> and produces the exit code. A build that reached
/// the pack phase is reported by <see cref="PluginPackReporter" /> itself, so <c>build</c> never restates
/// <c>pack</c>'s codes or exit codes in a second place.</summary>
internal static class PluginBuildReporter
{
	public static async Task<int> ReportAsync(CliConsole console,
		PluginBuildResult result,
		CancellationToken cancellationToken = default)
	{
		foreach (var warning in result.Warnings)
		{
			console.WriteWarning(warning.Code, warning.Message);
		}

		if (result.FailureReason is { } reason)
		{
			console.WriteError(PluginBuildFailureCode.For(reason),
				result.FailureMessage ?? "The build failed.",
				result.FailureDetail);

			return PluginBuildFailureExitCode.For(reason);
		}

		if (result.Pack is not { } pack)
		{
			console.WriteError(PluginBuildFailureCode.For(null), "The build produced no result.");

			return PluginBuildFailureExitCode.For(null);
		}

		if (pack.Success)
		{
			console.Info($"Built {string.Join(", ", result.BuiltRids)}.");
		}

		return await PluginPackReporter.ReportAsync(console, pack, showDigest: false, cancellationToken)
			.ConfigureAwait(false);
	}
}
