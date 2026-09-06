namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

internal static class PluginStubLocator
{
	public static string FindExecutable()
	{
		var repoRoot = FindRepoRoot(AppContext.BaseDirectory) ??
			throw new InvalidOperationException(
				"Could not locate the repository root (no MacroDeck.slnx found above the test output directory).");

		var configuration = AppContext.BaseDirectory.Contains("Debug", StringComparison.Ordinal) ? "Debug" : "Release";
		var fixtureBin = Path.Combine(repoRoot,
			"host",
			"tests",
			"fixtures",
			"MacroDeckHost.Tests.PluginStub",
			"bin",
			configuration,
			"net10.0");

		var candidateNames = OperatingSystem.IsWindows()
			? ["MacroDeckHost.Tests.PluginStub.exe"]
			: new[] { "MacroDeckHost.Tests.PluginStub" };

		foreach (var name in candidateNames)
		{
			var candidate = Path.Combine(fixtureBin, name);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new InvalidOperationException(
			$"MacroDeckHost.Tests.PluginStub was not found under '{fixtureBin}'. Build it first (it is part of " +
			"MacroDeck.slnx).");
	}

	private static string? FindRepoRoot(string startDirectory)
	{
		var directory = new DirectoryInfo(startDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		return null;
	}
}
