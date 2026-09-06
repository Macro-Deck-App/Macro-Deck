namespace MacroDeck.Plugin.Cli.Building;

internal readonly record struct PluginBuildRunResult(int ExitCode, string StandardOutput, string StandardError)
{
	public bool Success => ExitCode == 0;

	/// <summary>Both streams, non-empty parts only. A build tool's real failure text may be on either -
	/// MSBuild at low verbosity puts errors on stdout - so neither alone is enough to report a failure.</summary>
	public string? CombinedOutput
	{
		get
		{
			var parts = new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(part => part.Length > 0);
			var combined = string.Join(Environment.NewLine, parts);
			return combined.Length > 0 ? combined : null;
		}
	}
}

/// <summary>
/// The seam between <c>build</c> and a real build toolchain. Everything upstream of this interface -
/// config reading, RID selection, staging, entrypoint verification - is exercised by tests entirely
/// through a fake implementation: no test may launch a real build tool, which would be slow,
/// network-dependent, and different on every machine.
/// </summary>
internal interface IPluginBuildRunner
{
	/// <exception cref="Processes.ProcessLaunchException">The executable could not be launched at all, as
	/// opposed to launching and failing - which is a non-zero <see cref="PluginBuildRunResult.ExitCode" />.</exception>
	Task<PluginBuildRunResult> RunAsync(string executable,
		IReadOnlyList<string> arguments,
		string workingDirectory,
		CancellationToken cancellationToken);
}
