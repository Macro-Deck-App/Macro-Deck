namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>The subject a command was pointed at (a project, an executable, or an artifact) could not be
/// resolved into something launchable. Always maps to <see cref="ExitCode.InputUnreadable" /> - the
/// distinction the exit code table exists for is exactly "the tool could not get hold of the thing it was
/// asked to run", never a verdict about the plugin's own behaviour.</summary>
internal sealed class PluginSubjectException : Exception
{
	public PluginSubjectException(string code, string message, string? detail = null)
		: base(message)
	{
		Code = code;
		Detail = detail;
	}

	public string Code { get; }

	public string? Detail { get; }
}
