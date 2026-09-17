using System.CommandLine;
using MacroDeck.Plugin.Cli.Merging;

namespace MacroDeck.Plugin.Cli.Commands;

internal static class MergeCommand
{
	public static Command Create()
	{
		var artifactsArgument = new Argument<string[]>("artifacts")
		{
			Description = "The single-runtime .macroDeckPlugin packages to merge, for example from build --rid.",
			Arity = ArgumentArity.OneOrMore
		};
		var outputOption = new Option<string>("--output")
		{
			Description = "Directory to write the artifact to. The name comes from the plugin id and version.",
			DefaultValueFactory = _ => "."
		};
		var forceOption = new Option<bool>("--force") { Description = "Overwrite an existing artifact." };

		var command = new Command("merge",
			"Merge packages built for different runtime identifiers into one package.");
		command.Add(artifactsArgument);
		command.Add(outputOption);
		command.Add(forceOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);

			var result = await PluginMerger.MergeAsync(parseResult.GetValue(artifactsArgument) ?? [],
					parseResult.GetValue(outputOption) ?? ".",
					parseResult.GetValue(forceOption),
					cancellationToken: cancellationToken)
				.ConfigureAwait(false);

			return await PluginMergeReporter.ReportAsync(console, result, cancellationToken).ConfigureAwait(false);
		});

		return command;
	}
}
