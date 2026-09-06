using System.CommandLine;

namespace MacroDeck.Plugin.Cli;

/// <summary>
/// Options every command accepts. Only <see cref="VerbosityOption" /> and <see cref="NoColorOption" /> are
/// declared <c>Recursive</c> on the root command - the third documented global option, <c>--output</c>, is
/// deliberately not one of them: <c>pack</c> and <c>test</c> each already define their own
/// <c>--output &lt;path&gt;</c>, and a recursive <c>--output text|json</c> on the root command would
/// collide with both. <c>validate</c> and <c>inspect</c> instead each add their own local
/// <see cref="CreateOutputFormatOption" /> instance.
/// </summary>
internal static class GlobalOptions
{
	public static readonly Option<Verbosity> VerbosityOption = CreateVerbosityOption();

	public static readonly Option<bool> NoColorOption = new("--no-color")
	{
		Description = "Disable ANSI color in text output.",
		Recursive = true
	};

	/// <summary>A fresh <c>--output text|json</c> instance - an <see cref="Option{T}" /> instance may only
	/// ever belong to one command, so <c>validate</c> and <c>inspect</c> each need their own.</summary>
	public static Option<CliOutputFormat> CreateOutputFormatOption()
	{
		var option = new Option<CliOutputFormat>("--output")
		{
			Description = "How to render the result.",
			DefaultValueFactory = _ => CliOutputFormat.Text
		};

		option.CustomParser = result => CliOptionParsing.ParseToken(result,
			CliOutputFormat.Text,
			("text", CliOutputFormat.Text),
			("json", CliOutputFormat.Json));

		return option;
	}

	private static Option<Verbosity> CreateVerbosityOption()
	{
		var option = new Option<Verbosity>("--verbosity")
		{
			Description = "How much a command narrates while it works.",
			DefaultValueFactory = _ => Verbosity.Normal,
			Recursive = true
		};

		option.CustomParser = result => CliOptionParsing.ParseToken(result,
			Verbosity.Normal,
			("quiet", Verbosity.Quiet),
			("normal", Verbosity.Normal),
			("diagnostic", Verbosity.Diagnostic));

		return option;
	}
}
