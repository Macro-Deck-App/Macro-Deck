using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Processes;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// The <see cref="IPluginBuildRunner" /> double. No test may launch a real build tool: a real
/// <c>dotnet publish</c> would take minutes per runtime identifier, need a network, and produce different
/// bytes on every machine - none of which the behaviour under test depends on. <see cref="OnRun" /> stands
/// in for what a real tool would have written into its output directory, including a partial tree ahead of
/// a failure.
/// </summary>
internal sealed class FakePluginBuildRunner : IPluginBuildRunner
{
	public List<(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory)> Invocations { get; } =
		[];

	public PluginBuildRunResult Result { get; set; } = new(0, string.Empty, string.Empty);

	/// <summary>Keyed by the invocation's index, so a multi-target build can fail on its second target.</summary>
	public Dictionary<int, PluginBuildRunResult> ResultAt { get; } = [];

	/// <summary>Thrown instead of returning - the launch-failure path, which is an exception rather than an
	/// exit code.</summary>
	public Exception? ThrowOn { get; set; }

	/// <summary>Invoked with the executable, arguments and working directory before the result is returned.</summary>
	public Action<string, IReadOnlyList<string>, string>? OnRun { get; set; }

	public Task<PluginBuildRunResult> RunAsync(string executable,
		IReadOnlyList<string> arguments,
		string workingDirectory,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var index = Invocations.Count;
		Invocations.Add((executable, arguments, workingDirectory));

		if (ThrowOn is not null)
		{
			throw ThrowOn;
		}

		OnRun?.Invoke(executable, arguments, workingDirectory);

		return Task.FromResult(ResultAt.TryGetValue(index, out var result) ? result : Result);
	}

	/// <summary>The launch failure a real runner reports when the configured executable is not on PATH.</summary>
	public static ProcessLaunchException NotLaunchable(string fileName) => new(fileName);
}
