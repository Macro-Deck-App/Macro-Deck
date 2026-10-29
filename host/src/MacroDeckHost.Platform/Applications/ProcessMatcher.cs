using System.ComponentModel;
using System.Diagnostics;
using MacroDeckHost.Platform.ShellLinks;

namespace MacroDeckHost.Platform.Applications;

internal static class ProcessMatcher
{
	public static IReadOnlyList<Process> Find(string configuredPath)
	{
		if (string.IsNullOrWhiteSpace(configuredPath))
		{
			return [];
		}

		var candidates = CandidatePaths(configuredPath);
		var normalizedPaths = candidates.Select(Normalize).ToList();
		var names = candidates.Select(SafeFileNameWithoutExtension).Where(name => name.Length > 0).ToList();

		var byPath = new List<Process>();
		var byName = new List<Process>();

		foreach (var process in Process.GetProcesses())
		{
			var executablePath = TryGetExecutablePath(process);
			if (executablePath is not null
				&& normalizedPaths.Contains(Normalize(executablePath), StringComparer.OrdinalIgnoreCase))
			{
				byPath.Add(process);
			}
			else if (names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
			{
				byName.Add(process);
			}
			else
			{
				process.Dispose();
			}
		}

		var matches = byPath.Count > 0 ? byPath : byName;
		Dispose(byPath.Count > 0 ? byName : byPath);
		return matches;
	}

	public static bool IsRunning(string configuredPath)
	{
		var matches = Find(configuredPath);
		Dispose(matches);
		return matches.Count > 0;
	}

	public static void Dispose(IEnumerable<Process> processes)
	{
		foreach (var process in processes)
		{
			process.Dispose();
		}
	}

	public static string? TryGetExecutablePath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch (Win32Exception)
		{
			// Access denied (e.g. an elevated process while we run unelevated) - cannot read the path.
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static List<string> CandidatePaths(string configuredPath)
	{
		var target = TryReadShortcutTarget(configuredPath.Trim());
		return target is null ? [configuredPath] : [target, configuredPath];
	}

	private static string? TryReadShortcutTarget(string path)
	{
		if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
		{
			return null;
		}

		try
		{
			var link = WindowsShellLinkParser.TryParseFile(path);

			// With arguments (cmd.exe /k, Update.exe --processStart) the target alone is not the app the link starts.
			return link is { TargetPath: not null, Arguments: null } ? link.TargetPath : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static string Normalize(string path)
	{
		try
		{
			var full = Path.GetFullPath(path.Trim());
			return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return path.Trim();
		}
	}

	private static string SafeFileNameWithoutExtension(string path)
	{
		try
		{
			return Path.GetFileNameWithoutExtension(path);
		}
		catch
		{
			return string.Empty;
		}
	}
}
