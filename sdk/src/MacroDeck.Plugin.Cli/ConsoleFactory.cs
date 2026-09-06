using System.CommandLine;

namespace MacroDeck.Plugin.Cli;

/// <summary>Builds the <see cref="CliConsole" /> every command's action starts with, reading the two truly
/// global options off <paramref name="parseResult" />.</summary>
internal static class ConsoleFactory
{
	/// <summary>Writes wherever the invocation was configured to write. Those default to
	/// <see cref="Console.Out" />/<see cref="Console.Error" />, so a real run is unaffected - but a host that
	/// supplied its own writers (<c>CliEntryPoint.RunAsync</c>, and tests through it) sees a command's output
	/// instead of it escaping to the process console.</summary>
	public static CliConsole From(ParseResult parseResult) =>
		From(parseResult, parseResult.InvocationConfiguration.Output, parseResult.InvocationConfiguration.Error);

	/// <summary>As <see cref="From(ParseResult)" />, but writing to <paramref name="output" /> and
	/// <paramref name="error" /> instead of the real console - what <c>CliEntryPoint.RunAsync</c> uses for
	/// its own top-level diagnostics, so a caller that supplied its own writers (a test host, in
	/// particular) actually observes them.</summary>
	public static CliConsole From(ParseResult parseResult, TextWriter? output, TextWriter? error)
	{
		// Reading an option whose own parser rejected the value throws, so a console built to *report* a
		// parse error must not depend on that parse having succeeded - '--verbosity bogus' would otherwise
		// take down the process with the raw stack trace this tool exists to avoid.
		var verbosity = TryGetValue(parseResult, GlobalOptions.VerbosityOption, Verbosity.Normal);
		var noColor = TryGetValue(parseResult, GlobalOptions.NoColorOption, defaultValue: false);
		return new CliConsole(verbosity, noColor, output, error);
	}

	private static T TryGetValue<T>(ParseResult parseResult, Option<T> option, T defaultValue)
	{
		try
		{
			return parseResult.GetValue(option) ?? defaultValue;
		}
		catch (InvalidOperationException)
		{
			return defaultValue;
		}
	}
}
