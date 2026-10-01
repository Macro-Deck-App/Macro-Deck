namespace MacroDeck.Plugin.Cli.Tests.UnitTests.Support;

internal static class FixturePlugins
{
	public static string WellBehaved() => Find("MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin");

	public static string Misbehaving() => Find("MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin");

	private static string Find(string name)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		var configuration = AppContext.BaseDirectory.Contains("Debug", StringComparison.Ordinal) ? "Debug" : "Release";
		var executable = Path.Combine(directory!.FullName, "sdk", "tests", "fixtures", name, "bin", configuration, "net10.0",
			OperatingSystem.IsWindows() ? $"{name}.exe" : name);

		return File.Exists(executable)
			? executable
			: throw new InvalidOperationException($"{name} was not built; looked for '{executable}'.");
	}
}
