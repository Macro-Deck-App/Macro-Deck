using MacroDeckHost.Platform.Native;
using MacroDeckHost.Platform.ShellLinks;

namespace MacroDeckHost.Platform.Applications;

internal sealed record ApplicationTarget(
	IReadOnlyList<string> NormalizedPaths,
	IReadOnlyList<string> Names,
	bool IsPath)
{
	public static ApplicationTarget? Resolve(string configured)
	{
		if (string.IsNullOrWhiteSpace(configured))
		{
			return null;
		}

		var candidates = CandidatePaths(configured);
		var normalizedPaths = candidates.Select(Normalize).ToList();

		// A bundle executable is matched by path only: names such as Electron or stable are shared by
		// unrelated apps, and stopping by name would hit those.
		if (OperatingSystem.IsMacOS()
			&& MacOsBundles.IsAppBundle(configured)
			&& MacOsBundles.TryGetExecutablePath(configured.Trim()) is { } bundleExecutable)
		{
			normalizedPaths.Add(bundleExecutable);
		}

		var names = candidates.SelectMany(ProcessNames).Where(name => name.Length > 0).Distinct().ToList();
		var trimmed = configured.Trim();
		var isPath = trimmed.Contains(Path.DirectorySeparatorChar)
			|| trimmed.Contains(Path.AltDirectorySeparatorChar)
			|| Path.IsPathRooted(trimmed);
		return new ApplicationTarget(normalizedPaths, names, isPath);
	}

	public bool MatchesPath(string executablePath)
	{
		return NormalizedPaths.Contains(Normalize(executablePath), StringComparer.OrdinalIgnoreCase);
	}

	public bool MatchesName(string processName)
	{
		return Names.Contains(processName, StringComparer.OrdinalIgnoreCase);
	}

	public static string Normalize(string path)
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

	// A process name keeps every dot it has (python3.12, Microsoft.Photos); only a program extension is
	// left out of it, as Windows does for .exe.
	private static IEnumerable<string> ProcessNames(string path)
	{
		string fileName;
		try
		{
			fileName = Path.GetFileName(path.Trim());
		}
		catch
		{
			yield break;
		}

		yield return fileName;
		var extension = Path.GetExtension(fileName);
		if (IsProgramExtension(extension))
		{
			yield return fileName[..^extension.Length];
		}
	}

	private static bool IsProgramExtension(string extension)
	{
		return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".app", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
	}
}
