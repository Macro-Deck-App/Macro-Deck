using System.CommandLine;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin pack</c>: builds a <c>.macroDeckPlugin</c> artifact from a source tree, validating
/// the manifest through the same path as <c>validate</c> first. No <c>--output text|json</c> here (unlike
/// <c>validate</c>/<c>inspect</c>): <c>--output</c> already names the artifact path for this command, so a
/// second, differently-typed <c>--output</c> would collide with it. The actual packing lives in
/// <see cref="PluginPacker" />; reporting lives in <see cref="PluginPackReporter" /> - this file is option
/// parsing only.
/// </summary>
internal static class PackCommand
{
	public static Command Create()
	{
		var sourceOption = new Option<string>("--source")
			{ Description = "The payload directory to pack.", DefaultValueFactory = _ => "." };
		var manifestOption = new Option<string?>("--manifest")
			{ Description = "Path to manifest.json. Defaults to <source>/manifest.json." };
		var outputOption = new Option<string?>("--output")
			{ Description = "Where to write the artifact. Defaults to <id>-<version>.macroDeckPlugin." };
		var forceOption = new Option<bool>("--force") { Description = "Overwrite an existing output file." };
		var showDigestOption = new Option<bool>("--show-digest")
		{
			Description = "Also print the packed manifest's signable digest, base64-encoded - the exact bytes a " +
				"signature is computed over."
		};

		var command = new Command("pack", "Build a .macroDeckPlugin artifact from a source tree.");
		command.Add(sourceOption);
		command.Add(manifestOption);
		command.Add(outputOption);
		command.Add(forceOption);
		command.Add(showDigestOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var source = parseResult.GetValue(sourceOption) ?? ".";
			var manifestPath = parseResult.GetValue(manifestOption) ?? Path.Combine(source, "manifest.json");
			var explicitOutput = parseResult.GetValue(outputOption);
			var force = parseResult.GetValue(forceOption);
			var showDigest = parseResult.GetValue(showDigestOption);

			console.Trace($"Packing '{source}' with manifest '{manifestPath}'.");

			var result = await PluginPacker.PackAsync(source,
					manifestPath,
					manifest => explicitOutput ??
						$"{manifest.Id}-{manifest.Version}{PluginArtifactFiles.MacroDeckPluginExtension}",
					force,
					cancellationToken)
				.ConfigureAwait(false);

			return await PluginPackReporter.ReportAsync(console, result, showDigest, cancellationToken)
				.ConfigureAwait(false);
		});

		return command;
	}
}
