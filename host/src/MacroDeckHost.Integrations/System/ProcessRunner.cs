using System.Diagnostics;

namespace MacroDeckHost.Integrations.System;

internal static class ProcessRunner
{
	public static async Task<string> RunAsync(
		string fileName,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken = default)
	{
		var startInfo = new ProcessStartInfo(fileName)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var process = Process.Start(startInfo) ??
			throw new InvalidOperationException($"Failed to start '{fileName}'.");

		var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);
		return output;
	}

	public readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError)
	{
		public bool Succeeded => ExitCode == 0;
	}

	public static Task<ProcessResult> RunWithResultAsync(
		string fileName,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken = default)
		=> RunWithResultAsync(fileName, arguments, null, cancellationToken);

	public static async Task<ProcessResult> RunWithResultAsync(
		string fileName,
		IReadOnlyList<string> arguments,
		IReadOnlyDictionary<string, string?>? environment,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo(fileName)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
		{
			if (value is null)
			{
				startInfo.Environment.Remove(key);
			}
			else
			{
				startInfo.Environment[key] = value;
			}
		}

		using var process = Process.Start(startInfo) ??
			throw new InvalidOperationException($"Failed to start '{fileName}'.");

		var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
		try
		{
			await Task.WhenAll(outputTask, errorTask);
			await process.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			// A hung child must not outlive the caller that gave up on it.
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
			{
			}

			throw;
		}

		return new ProcessResult(process.ExitCode, outputTask.Result, errorTask.Result);
	}

	public static bool CommandExists(string command)
	{
		try
		{
			var locator = OperatingSystem.IsWindows() ? "where.exe" : "which";
			var startInfo = new ProcessStartInfo(locator)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			startInfo.ArgumentList.Add(command);

			using var process = Process.Start(startInfo);
			if (process is null)
			{
				return false;
			}

			process.WaitForExit(2000);
			return process is { HasExited: true, ExitCode: 0 };
		}
		catch
		{
			return false;
		}
	}
}
