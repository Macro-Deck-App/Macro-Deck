namespace MacroDeckHost.Infrastructure.Autostart;

public sealed class MacOsAutostartRegistrar : FileAutostartRegistrar
{
	public MacOsAutostartRegistrar(string launchAgentsDirectory)
		: base(launchAgentsDirectory)
	{
	}

	public static string DefaultDirectory()
		=> Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

	protected override string FileName => $"{AutostartEntryContent.LaunchAgentLabel}.plist";

	protected override string BuildContent(AutostartRegistration registration)
		=> AutostartEntryContent.BuildLaunchAgentPlist(registration);

	protected override AutostartRegistration? ParseContent(string content)
		=> AutostartEntryContent.ParseLaunchAgentPlist(content);
}
