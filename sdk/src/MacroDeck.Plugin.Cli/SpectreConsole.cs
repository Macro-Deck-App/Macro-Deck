using Spectre.Console;

namespace MacroDeck.Plugin.Cli;

/// <summary>Builds the raw-key <see cref="IAnsiConsole" /> the wizard's checkbox prompt needs.</summary>
internal static class SpectreConsole
{
	/// <summary>Returns <c>null</c> unless the terminal reports the ANSI and interaction support a list
	/// prompt needs - stdin is the caller's business, see <see cref="Scaffolding.CliPromptReader" />.
	/// Spectre's list prompts throw <see cref="NotSupportedException" /> rather than degrade when ANSI or
	/// raw-key support is missing, so this has to be a pre-check on the detected capabilities rather than a
	/// handler around the exception.</summary>
	public static IAnsiConsole? TryCreate(TextWriter output, bool noColor)
	{
		var console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = AnsiSupport.Detect,
			ColorSystem = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
			Interactive = InteractionSupport.Detect,
			Out = new AnsiConsoleOutput(output)
		});

		return console.Profile.Capabilities is { Interactive: true, Ansi: true } ? console : null;
	}
}
