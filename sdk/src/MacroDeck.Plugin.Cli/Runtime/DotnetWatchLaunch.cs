using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Runtime;

internal static class DotnetWatchLaunch
{
	public static ProcessLaunch For(string projectPath)
	{
		var fullPath = Path.GetFullPath(projectPath);
		var projectDirectory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath)!;

		// The project directory is the content root the SDK reads manifest.json from, as with dotnet run.
		// No launch profile: its environment would override the host URL and state directory run composed.
		return new ProcessLaunch(PluginLaunchSpec.DotnetMuxerPath(),
			["watch", "--non-interactive", "--project", fullPath, "run", "--no-launch-profile"],
			projectDirectory);
	}
}
