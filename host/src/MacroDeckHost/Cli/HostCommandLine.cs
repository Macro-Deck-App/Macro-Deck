namespace MacroDeckHost.Cli;

public enum HostCommand
{
	Run,
	Help,
	Version,
	ListPlugins
}

public sealed record HostCommandLine(HostCommand Command, bool DisablePlugins, string[] RemainingArguments)
{
	public const string HelpOption = "--help";
	public const string ShortHelpOption = "-h";
	public const string VersionOption = "--version";
	public const string ListPluginsOption = "--list-plugins";
	public const string DisablePluginsOption = "--disable-plugins";

	public static HostCommandLine Parse(IReadOnlyList<string> arguments)
	{
		var help = false;
		var version = false;
		var listPlugins = false;
		var disablePlugins = false;
		var remaining = new List<string>();

		foreach (var argument in arguments)
		{
			switch (argument)
			{
				case HelpOption or ShortHelpOption:
					help = true;
					break;
				case VersionOption:
					version = true;
					break;
				case ListPluginsOption:
					listPlugins = true;
					break;
				case DisablePluginsOption:
					disablePlugins = true;
					break;
				default:
					remaining.Add(argument);
					break;
			}
		}

		var command = help
			? HostCommand.Help
			: version
				? HostCommand.Version
				: listPlugins
					? HostCommand.ListPlugins
					: HostCommand.Run;

		return new HostCommandLine(command, disablePlugins, remaining.ToArray());
	}
}
