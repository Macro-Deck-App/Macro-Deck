using System.ComponentModel;
using System.Diagnostics;

namespace MacroDeckHost.Integrations.System.Application;

internal static class ProcessMatcher
{
	public static IReadOnlyList<Process> Find(string configuredPath)
	{
		if (string.IsNullOrWhiteSpace(configuredPath))
		{
			return [];
		}

		var normalizedPath = Normalize(configuredPath);
		var nameFallback = SafeFileNameWithoutExtension(configuredPath);

		var byPath = new List<Process>();
		var byName = new List<Process>();

		foreach (var process in Process.GetProcesses())
		{
			var executablePath = TryGetExecutablePath(process);
			if (executablePath is not null &&
				string.Equals(Normalize(executablePath), normalizedPath, StringComparison.OrdinalIgnoreCase))
			{
				byPath.Add(process);
			}
			else if (!string.IsNullOrEmpty(nameFallback) &&
				string.Equals(process.ProcessName, nameFallback, StringComparison.OrdinalIgnoreCase))
			{
				byName.Add(process);
			}
		}

		return byPath.Count > 0 ? byPath : byName;
	}

	public static bool IsRunning(string configuredPath) => Find(configuredPath).Count > 0;

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
