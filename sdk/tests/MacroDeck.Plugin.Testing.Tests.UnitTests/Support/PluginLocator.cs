namespace MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

/// <summary>
/// Locates the built executables A15/A16/A18 launch as real processes. Walks up from the running test
/// assembly to the repo root (identified by <c>MacroDeck.slnx</c>) and back down into each fixture's own
/// build output - solution-relative, mirroring
/// <c>host/tests/MacroDeckHost.Tests.UnitTests/Plugins/Runtime/PluginStubLocator.cs</c>.
/// </summary>
internal static class PluginLocator
{
	/// <summary>The fixture's built executable - the well-behaved subject.</summary>
	public static string FindWellBehavedPluginExecutable()
		=> FindExecutable(["sdk", "tests", "fixtures", "MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin"],
			"MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin");

	/// <summary>The misbehaving fixture's built executable - see <c>MisbehaviorFlags</c> for what <c>MACRODECK_MISBEHAVE</c> understands.</summary>
	public static string FindMisbehavingPluginExecutable()
		=> FindExecutable(["sdk", "tests", "fixtures", "MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin"],
			"MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin");

	private static string FindExecutable(string[] projectDirectorySegments, string assemblyName)
	{
		var repoRoot = FindRepoRoot(AppContext.BaseDirectory) ??
			throw new InvalidOperationException(
				"Could not locate the repository root (no MacroDeck.slnx found above the test output directory).");

		var configuration = AppContext.BaseDirectory.Contains("Debug", StringComparison.Ordinal) ? "Debug" : "Release";
		var relative = Path.Combine(projectDirectorySegments);
		var bin = Path.Combine(repoRoot, relative, "bin", configuration, "net10.0");

		var candidateName = OperatingSystem.IsWindows() ? $"{assemblyName}.exe" : assemblyName;
		var candidate = Path.Combine(bin, candidateName);

		if (File.Exists(candidate))
		{
			return candidate;
		}

		throw new InvalidOperationException(
			$"{assemblyName} was not found under '{bin}'. Build it first (it is part of MacroDeck.slnx).");
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
