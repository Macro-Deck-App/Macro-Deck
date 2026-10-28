using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Processes;

// The bootstrapper gives the host piped stdio and kills the host's process group on a forced stop.
// Programs started for the user get their own session and /dev/null stdio so they outlive Macro Deck.
internal static class DetachedProcess
{
	private const int ReaperStackSize = 64 * 1024;
	private const string MacOsOpener = "/usr/bin/open";

	private static readonly string[] _linuxOpeners = ["xdg-open", "gnome-open", "kfmclient"];

	public static void Start(ProcessStartInfo startInfo)
	{
		if (OperatingSystem.IsWindows())
		{
			Process.Start(startInfo);
			return;
		}

		_ = Spawn(startInfo);
	}

	// Resolves the program the way Process.Start does on Unix, so a launch finds and opens exactly
	// what it did before. Throws before returning when the program cannot be started.
	[UnsupportedOSPlatform("windows")]
	public static Task<int> Spawn(ProcessStartInfo startInfo)
	{
		AppImageEnvironment.RestoreOutside(startInfo);
		var environment = startInfo.Environment
			.Where(variable => variable.Value is not null)
			.Select(variable => $"{variable.Key}={variable.Value}")
			.ToArray();
		var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
			? null
			: startInfo.WorkingDirectory;

		var pid = startInfo.UseShellExecute
			? SpawnShellExecute(startInfo, environment, workingDirectory)
			: SpawnProgram(startInfo, environment, workingDirectory);
		return Reap(pid);
	}

	[UnsupportedOSPlatform("windows")]
	private static int SpawnProgram(ProcessStartInfo startInfo, string[] environment, string? workingDirectory)
	{
		var program = ResolvePath(startInfo.FileName)
			?? throw new Win32Exception(PosixSpawn.NoSuchFile, $"'{startInfo.FileName}' was not found.");
		if (Directory.Exists(program))
		{
			throw new Win32Exception(PosixSpawn.IsADirectory, $"'{program}' is a directory.");
		}

		return SpawnOrThrow(program, Arguments(startInfo), environment, workingDirectory);
	}

	[UnsupportedOSPlatform("windows")]
	private static int SpawnShellExecute(ProcessStartInfo startInfo, string[] environment, string? workingDirectory)
	{
		var executable = ResolveExecutableForShellExecute(startInfo.FileName, workingDirectory);
		if (executable is not null)
		{
			var error = PosixSpawn.SpawnDetached(executable,
				Arguments(startInfo),
				environment,
				workingDirectory,
				out var pid);
			if (error == 0)
			{
				return pid;
			}

			// Process.Start opens a file it cannot execute with the default application: every file
			// on a mounted Windows file system carries the execute bit.
			if (error != PosixSpawn.NotExecutable)
			{
				throw new Win32Exception(error);
			}
		}

		var opener = OpenerPath()
			?? throw new Win32Exception(PosixSpawn.NoSuchFile, "No program to open files with was found.");
		List<string> arguments = [opener];
		if (opener.EndsWith("/kfmclient", StringComparison.Ordinal))
		{
			arguments.Add("openURL");
		}

		arguments.Add(startInfo.FileName);
		return SpawnOrThrow(opener, arguments, environment, workingDirectory);
	}

	[UnsupportedOSPlatform("windows")]
	private static int SpawnOrThrow(
		string program,
		IReadOnlyList<string> arguments,
		string[] environment,
		string? workingDirectory)
	{
		var error = PosixSpawn.SpawnDetached(program, arguments, environment, workingDirectory, out var pid);
		if (error != 0)
		{
			throw new Win32Exception(error);
		}

		return pid;
	}

	private static List<string> Arguments(ProcessStartInfo startInfo)
	{
		List<string> arguments = [startInfo.FileName];
		arguments.AddRange(startInfo.ArgumentList.Count > 0
			? startInfo.ArgumentList
			: CommandLineArguments.Split(startInfo.Arguments));
		return arguments;
	}

	[UnsupportedOSPlatform("windows")]
	private static string? ResolvePath(string fileName)
	{
		if (Path.IsPathRooted(fileName))
		{
			return fileName;
		}

		if (Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } processDirectory)
		{
			var besideHost = Path.Combine(processDirectory, fileName);
			if (File.Exists(besideHost))
			{
				return besideHost;
			}
		}

		var inCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), fileName);
		return File.Exists(inCurrentDirectory) ? inCurrentDirectory : FindProgramInPath(fileName);
	}

	[UnsupportedOSPlatform("windows")]
	private static string? ResolveExecutableForShellExecute(string fileName, string? workingDirectory)
	{
		string? resolved;
		if (Path.IsPathRooted(fileName))
		{
			resolved = File.Exists(fileName) ? fileName : null;
		}
		else if (Uri.TryCreate(fileName, UriKind.Absolute, out var uri))
		{
			resolved = uri.IsFile && uri.Host.Length == 0 && File.Exists(uri.LocalPath) ? uri.LocalPath : null;
		}
		else
		{
			var directory = workingDirectory is null
				? Directory.GetCurrentDirectory()
				: Path.GetFullPath(workingDirectory);
			var inDirectory = Path.Combine(directory, fileName);
			resolved = File.Exists(inDirectory) ? inDirectory : FindProgramInPath(fileName);
		}

		return resolved is not null && PosixSpawn.IsExecutable(resolved) ? resolved : null;
	}

	[UnsupportedOSPlatform("windows")]
	private static string? OpenerPath()
	{
		return OperatingSystem.IsMacOS()
			? MacOsOpener
			: _linuxOpeners.Select(FindProgramInPath).FirstOrDefault(path => path is not null);
	}

	[UnsupportedOSPlatform("windows")]
	private static string? FindProgramInPath(string program)
	{
		var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		return searchPath.Split(':', StringSplitOptions.RemoveEmptyEntries)
			.Select(directory => Path.Combine(directory, program))
			.FirstOrDefault(PosixSpawn.IsExecutable);
	}

	[UnsupportedOSPlatform("windows")]
	private static Task<int> Reap(int pid)
	{
		var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var reaper = new Thread(() => exited.SetResult(PosixSpawn.WaitForExit(pid)), ReaperStackSize)
		{
			IsBackground = true,
			Name = "Detached process reaper"
		};
		reaper.Start();
		return exited.Task;
	}
}
