namespace MacroDeck.Plugin.Cli.Rendering;

internal static class BrowserLocator
{
	public const string EnvironmentVariable = "MACRODECK_BROWSER";

	private static readonly string[] _macOsPaths =
	[
		"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
		"/Applications/Chromium.app/Contents/MacOS/Chromium",
		"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
	];

	private static readonly string[] _windowsRoots = ["ProgramFiles", "ProgramFiles(x86)", "LocalAppData"];

	private static readonly string[] _pathNames =
		["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge", "chrome"];

	public static string? Find(string? explicitPath, Func<string, string?> environment, Func<string, bool> exists)
	{
		if (!string.IsNullOrWhiteSpace(explicitPath))
		{
			return exists(explicitPath) ? explicitPath : null;
		}

		if (environment(EnvironmentVariable) is { Length: > 0 } fromEnvironment)
		{
			return exists(fromEnvironment) ? fromEnvironment : null;
		}

		foreach (var candidate in WellKnownPaths(environment))
		{
			if (exists(candidate))
			{
				return candidate;
			}
		}

		foreach (var directory in (environment("PATH") ?? string.Empty).Split(Path.PathSeparator,
			StringSplitOptions.RemoveEmptyEntries))
		{
			foreach (var name in _pathNames)
			{
				var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name);

				if (exists(candidate))
				{
					return candidate;
				}
			}
		}

		return null;
	}

	private static IEnumerable<string> WellKnownPaths(Func<string, string?> environment)
	{
		if (OperatingSystem.IsMacOS())
		{
			return _macOsPaths;
		}

		if (!OperatingSystem.IsWindows())
		{
			return [];
		}

		return _windowsRoots
			.Select(environment)
			.Where(root => !string.IsNullOrEmpty(root))
			.SelectMany(root => new[]
			{
				Path.Combine(root!, "Google", "Chrome", "Application", "chrome.exe"),
				Path.Combine(root!, "Microsoft", "Edge", "Application", "msedge.exe")
			});
	}
}
