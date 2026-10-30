namespace MacroDeckHost.Tests.SystemTests.Support;

internal static class BuildOutputs
{
	// Lets CI point the suite at a staged host instead of the plain build output, as the E2E suite does.
	public const string HostBinaryEnvironmentVariable = "MACRO_DECK_HOST_BINARY";

	public const string WellBehavedAssemblyName = "MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin";

	public const string MisbehavingAssemblyName = "MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin";

	private static string Configuration
		=> AppContext.BaseDirectory.Contains("Debug", StringComparison.Ordinal) ? "Debug" : "Release";

	public static string HostExecutable()
	{
		var staged = Environment.GetEnvironmentVariable(HostBinaryEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(staged))
		{
			return staged;
		}

		var name = OperatingSystem.IsWindows() ? "MacroDeckHost.exe" : "MacroDeckHost";
		return Existing(Path.Combine(BinDirectory("host", "src", "MacroDeckHost"), name));
	}

	public static string WellBehavedBuildDirectory()
	{
		return FixtureBuildDirectory(WellBehavedAssemblyName);
	}

	public static string MisbehavingBuildDirectory()
	{
		return FixtureBuildDirectory(MisbehavingAssemblyName);
	}

	private static string FixtureBuildDirectory(string assemblyName)
	{
		var directory = BinDirectory("sdk", "tests", "fixtures", assemblyName);
		Existing(Path.Combine(directory, assemblyName + ".dll"));
		return directory;
	}

	private static string BinDirectory(params string[] projectSegments)
	{
		return Path.Combine(RepositoryRoot(), Path.Combine(projectSegments), "bin", Configuration, "net10.0");
	}

	private static string Existing(string path)
	{
		if (!File.Exists(path))
		{
			throw new InvalidOperationException($"'{path}' does not exist. Build MacroDeck.slnx first.");
		}

		return path;
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException("No MacroDeck.slnx above the test output directory.");
	}
}
