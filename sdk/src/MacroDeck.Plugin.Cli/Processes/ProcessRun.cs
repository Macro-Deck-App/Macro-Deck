using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeck.Plugin.Cli.Processes;

internal readonly record struct ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
	public bool Success => ExitCode == 0;

	/// <summary>The non-empty parts of stdout and stderr, in that order - a tool's real failure text is
	/// often on one or the other depending on how it was invoked, never guaranteed which.</summary>
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

/// <summary>Thrown when a process could not be started at all - the executable is missing or not
/// launchable - as opposed to starting and failing, which is a <see cref="ProcessRunResult" /> with a
/// non-zero exit code. Deliberately toolchain-neutral: <c>new</c> translates it into its own
/// <c>dotnet-not-found</c> diagnostic, and <c>build</c> into one naming the runtime identifier whose build
/// recipe could not be launched.</summary>
internal sealed class ProcessLaunchException(string fileName)
	: Exception($"Could not launch '{fileName}'.")
{
	public string FileName { get; } = fileName;
}

/// <summary>The run-and-capture helper for every external tool this CLI shells out to. Arguments are
/// always passed as a list, never a command string, so nothing is evaluated by a shell.</summary>
internal static class ProcessRun
{
	public static async Task<ProcessRunResult> RunAsync(string fileName,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken,
		string? workingDirectory = null)
	{
		var startInfo = new ProcessStartInfo(fileName)
			{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

		if (workingDirectory is not null)
		{
			startInfo.WorkingDirectory = workingDirectory;
		}

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		Process process;
		try
		{
			process = Process.Start(startInfo) ?? throw new ProcessLaunchException(fileName);
		}
		catch (Win32Exception)
		{
			throw new ProcessLaunchException(fileName);
		}

		using (process)
		{
			var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
			var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

			try
			{
				await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// Disposing only releases the handle. An abandoned 'dotnet new install' would keep running and
				// keep writing to the machine-global template store long after Ctrl-C returned the prompt.
				TryKill(process);
				throw;
			}

			var stdout = await stdoutTask.ConfigureAwait(false);
			var stderr = await stderrTask.ConfigureAwait(false);

			return new ProcessRunResult(process.ExitCode, stdout, stderr);
		}
	}

	private static void TryKill(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
		{
		}
	}
}
