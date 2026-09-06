namespace MacroDeckHost.Infrastructure.Autostart;

public sealed class LinuxAutostartRegistrar : FileAutostartRegistrar
{
	public LinuxAutostartRegistrar(string autostartDirectory)
		: base(autostartDirectory)
	{
	}

	public static string DefaultDirectory()
	{
		var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if (string.IsNullOrWhiteSpace(configHome))
		{
			configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
		}

		return Path.Combine(configHome, "autostart");
	}

	protected override string FileName => $"{AutostartEntryContent.DesktopEntryName}.desktop";

	protected override string BuildContent(AutostartRegistration registration)
		=> AutostartEntryContent.BuildDesktopEntry(registration);

	protected override AutostartRegistration? ParseContent(string content)
	{
		var execLine = content.Split('\n')
			.Select(line => line.Trim())
			.FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal));

		return AutostartEntryContent.ParseCommandLine(execLine?["Exec=".Length..]);
	}
}
