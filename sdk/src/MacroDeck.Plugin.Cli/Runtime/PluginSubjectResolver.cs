using System.Diagnostics;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// Resolves the <c>--project</c> and <c>--executable</c> subject selectors <c>run</c> and <c>test</c>
/// share into a <see cref="PluginLaunchSpec" />. The third selector, <c>--artifact</c>, is deliberately
/// not handled here: <c>run</c> reaches it through <see cref="PluginLaunchSpec.ForArtifactAsync" /> and
/// <c>test</c> through <c>ConformanceSubject.Artifact</c>, both already-reused SDK entry points that do
/// their own resolution and caching.
/// </summary>
internal static class PluginSubjectResolver
{
	/// <summary>Builds a launch spec from an already-built executable or framework-dependent assembly,
	/// deciding which by extension alone: a <c>.dll</c> launches through the <c>dotnet</c> muxer, anything
	/// else launches directly.</summary>
	public static PluginLaunchSpec ResolveExecutable(string executablePath)
	{
		if (!File.Exists(executablePath))
		{
			throw new PluginSubjectException("executable-not-found",
				$"No executable at '{CliText.DisplayPath(executablePath)}'.");
		}

		return executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
			? PluginLaunchSpec.ForDotnet(executablePath)
			: PluginLaunchSpec.ForExecutable(executablePath);
	}

	/// <summary>
	/// Builds a project first via <c>dotnet build &lt;project&gt; -t:Build -getProperty:TargetPath</c> - a
	/// directory is accepted too, exactly as <c>dotnet build</c> itself resolves a directory containing
	/// exactly one project file - then resolves the printed path the same way
	/// <see cref="ResolveExecutable" /> does.
	/// </summary>
	public static async Task<PluginLaunchSpec> ResolveProjectAsync(string projectPath,
		CliConsole console,
		CancellationToken cancellationToken)
	{
		var targetPath = await GetTargetPathAsync(projectPath, console, cancellationToken).ConfigureAwait(false);
		return ResolveExecutable(targetPath);
	}

	private static async Task<string> GetTargetPathAsync(string projectPath,
		CliConsole console,
		CancellationToken cancellationToken)
	{
		if (Directory.Exists(projectPath) &&
			!Directory.EnumerateFiles(projectPath, "*.csproj")
				.Concat(Directory.EnumerateFiles(projectPath, "*.fsproj"))
				.Concat(Directory.EnumerateFiles(projectPath, "*.vbproj"))
				.Any())
		{
			throw new PluginSubjectException("project-not-found",
				$"No project file in '{CliText.DisplayPath(projectPath)}'.");
		}

		var startInfo = new ProcessStartInfo("dotnet")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		startInfo.ArgumentList.Add("build");
		startInfo.ArgumentList.Add(projectPath);
		startInfo.ArgumentList.Add("-nologo");
		startInfo.ArgumentList.Add("-verbosity:quiet");

		// -getProperty on its own puts MSBuild in evaluation-only mode: it prints where the assembly would
		// land and returns without producing it, which left --project failing on a clean tree with a
		// build-output-missing error for a build that never ran. Asking for a target explicitly is what
		// makes MSBuild execute the build and still print only the requested property.
		startInfo.ArgumentList.Add("-t:Build");
		startInfo.ArgumentList.Add("-getProperty:TargetPath");

		console.Trace($"Running: dotnet build {projectPath} -t:Build -getProperty:TargetPath");

		using var process = Process.Start(startInfo) ??
			throw new PluginSubjectException("dotnet-not-found", "Failed to start 'dotnet build'.");

		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

		var stdout = await stdoutTask.ConfigureAwait(false);
		var stderr = await stderrTask.ConfigureAwait(false);

		if (process.ExitCode != 0)
		{
			// At -verbosity:quiet, MSBuild sends a failing build's own errors to stdout, not stderr - stderr
			// is usually empty on failure. DescribeBuildOutput combines whichever of the two actually has
			// something to say, so the user sees the real MSBuild error instead of an empty message. The
			// internal -getProperty:TargetPath command line stays out of the user-facing message; it is
			// plumbing, not something a plugin author asked for.
			throw new PluginSubjectException("build-failed",
				$"'dotnet build {CliText.DisplayPath(projectPath)}' failed with exit code {process.ExitCode}.",
				DescribeBuildOutput(stdout, stderr));
		}

		// -getProperty prints only the requested value (plus a trailing newline) on success.
		var targetPath = stdout.Trim();

		if (targetPath.Length == 0 || !File.Exists(targetPath))
		{
			throw new PluginSubjectException("build-output-missing",
				$"'dotnet build' for '{CliText.DisplayPath(projectPath)}' did not resolve to an existing file " +
				$"(got '{targetPath}').");
		}

		return targetPath;
	}

	/// <summary>Combines the non-empty parts of a failed build's stdout and stderr into one detail block, in
	/// that order - pure and separated from <see cref="GetTargetPathAsync" /> so it is testable without
	/// spawning <c>dotnet</c>. Returns <see langword="null" /> when both are empty, so
	/// <see cref="CliConsole.WriteError" /> never prints a blank detail line.</summary>
	internal static string? DescribeBuildOutput(string stdout, string stderr)
	{
		var parts = new[] { stdout.Trim(), stderr.Trim() }.Where(part => part.Length > 0);
		var combined = string.Join(Environment.NewLine, parts);
		return combined.Length > 0 ? combined : null;
	}
}
