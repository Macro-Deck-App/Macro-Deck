using System.CommandLine;
using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// Wires up <c>build</c>: option parsing only. Building, staging and entrypoint verification live in
/// <see cref="PluginBuilder" />, packaging in <see cref="Packing.PluginPacker" /> - the same implementation
/// <c>pack</c> uses - and reporting in <see cref="PluginBuildReporter" />.
/// </summary>
internal static class BuildCommand
{
	public static Command Create(IPluginBuildRunner? runner = null)
	{
		var sourceOption = new Option<string>("--source")
			{ Description = "The plugin project directory to build.", DefaultValueFactory = _ => "." };
		var manifestOption = new Option<string?>("--manifest")
			{ Description = "Path to manifest.json. Defaults to <source>/manifest.json." };
		var buildConfigOption = new Option<string?>("--build-config")
		{
			Description =
				$"Path to {PluginBuildConfigReader.FileName}. Defaults to the file beside the manifest."
		};
		var ridOption = new Option<string?>("--rid")
		{
			Description = "Build only this runtime identifier, which the manifest must declare. " +
				"Defaults to building every declared runtime identifier.",
			HelpName = "rid"
		};
		var outputOption = new Option<string>("--output")
		{
			// A directory, unlike pack's --output: the artifact name is derived from the plugin id and
			// version so a build always lands somewhere predictable.
			Description = "Directory to write the artifact to. The name comes from the plugin id and version.",
			DefaultValueFactory = _ => "."
		};
		var forceOption = new Option<bool>("--force") { Description = "Overwrite an existing artifact." };

		var command = new Command("build",
			"Build every runtime identifier the manifest declares and package the result.");
		command.Add(sourceOption);
		command.Add(manifestOption);
		command.Add(buildConfigOption);
		command.Add(ridOption);
		command.Add(outputOption);
		command.Add(forceOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var source = parseResult.GetValue(sourceOption) ?? ".";
			var manifestPath = parseResult.GetValue(manifestOption) ??
				Path.Combine(source, PluginArtifactFiles.ManifestFileName);

			// Defaulted from the manifest's directory, not --source: 'new' writes the build config beside the
			// manifest, which is not necessarily the project root a caller passes as --source.
			var buildConfigPath = parseResult.GetValue(buildConfigOption) ??
				Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? source,
					PluginBuildConfigReader.FileName);

			var request = new PluginBuildRequest
			{
				SourceDirectory = source,
				ManifestPath = manifestPath,
				BuildConfigPath = buildConfigPath,
				Rid = parseResult.GetValue(ridOption),
				OutputDirectory = parseResult.GetValue(outputOption) ?? ".",
				Force = parseResult.GetValue(forceOption)
			};

			console.Trace($"Building '{source}' with manifest '{manifestPath}' and build configuration " +
				$"'{buildConfigPath}'.");

			var result = await PluginBuilder.BuildAsync(request,
					runner ?? new ProcessPluginBuildRunner(),
					rid => console.Info($"Building {rid}..."),
					cancellationToken)
				.ConfigureAwait(false);

			return await PluginBuildReporter.ReportAsync(console, result, cancellationToken).ConfigureAwait(false);
		});

		return command;
	}
}
