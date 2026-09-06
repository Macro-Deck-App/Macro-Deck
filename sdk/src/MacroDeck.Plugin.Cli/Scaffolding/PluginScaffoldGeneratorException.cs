namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>Raised by an <see cref="IPluginScaffoldGenerator" /> for a failure that is not an ordinary
/// "the tool ran and reported a problem" result - today only <c>dotnet</c> itself failing to launch at
/// all, which every generator call site can hit, real or faked.</summary>
internal sealed class PluginScaffoldGeneratorException(
	PluginScaffoldFailureReason reason,
	string message,
	string? detail = null) : Exception(message)
{
	public PluginScaffoldFailureReason Reason { get; } = reason;

	public string? Detail { get; } = detail;
}
