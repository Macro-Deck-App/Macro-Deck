namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// The entry point: <c>MacroDeckPlugin.CreatePlugin(args)</c>.
///
/// <para>
/// Named <c>MacroDeckPlugin</c> rather than the shorter <c>MacroDeck</c> because <c>MacroDeck</c> is
/// already a namespace root in this assembly's dependencies. C# resolves a simple name to a namespace
/// in scope before a type pulled in by a <c>using</c>, so a type called <c>MacroDeck</c> would be
/// unreachable from exactly the file that needs it - a <c>Program.cs</c> with top-level statements.
/// </para>
/// </summary>
public static class MacroDeckPlugin
{
	/// <summary>Starts building a plugin, taking configuration from the command line as well.</summary>
	public static PluginHostBuilder CreatePlugin(string[] args) => new(args);

	/// <summary>Starts building a plugin.</summary>
	public static PluginHostBuilder CreatePlugin() => new([]);
}
