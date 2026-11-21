using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeckHost.Platform.Applications;

internal static class ProcessMatcher
{
	public static IReadOnlyList<Process> Find(string configuredPath)
	{
		if (ApplicationTarget.Resolve(configuredPath) is not { } target)
		{
			return [];
		}

		var byPath = new List<Process>();
		var byName = new List<Process>();

		foreach (var process in Process.GetProcesses())
		{
			var executablePath = TryGetExecutablePath(process);
			if (executablePath is not null && target.MatchesPath(executablePath))
			{
				byPath.Add(process);
			}
			else if (target.MatchesName(process.ProcessName))
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
}
