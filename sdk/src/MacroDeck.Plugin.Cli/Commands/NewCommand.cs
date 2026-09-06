using System.CommandLine;
using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin new</c>: manages the plugin project template, collects plugin metadata
/// interactively or from flags, and produces a project whose <c>manifest.json</c> and
/// <c>macrodeck-build.json</c> are ready to build. Option parsing only - collection lives in
/// <see cref="PluginScaffoldWizard" />, scaffolding in <see cref="PluginScaffolder" />, matching the
/// existing <c>Commands/PackCommand.cs</c> + <c>Packing/PluginPacker.cs</c> split.
/// </summary>
internal static class NewCommand
{
	public static Command Create(CliPromptReader? prompt = null, IPluginScaffoldGenerator? scaffoldGenerator = null)
	{
		var nameOption = new Option<string?>("--name") { Description = "The plugin's human-readable display name." };
		var idOption = new Option<string?>("--id")
			{ Description = "The plugin's reverse-domain id, e.g. 'com.example.my-plugin'." };
		var publisherOption = new Option<string?>("--publisher") { Description = "The publisher's display name." };
		var descriptionOption = new Option<string?>("--description")
			{ Description = "A short description of the plugin." };
		var repositoryOption = new Option<string?>("--repository") { Description = "The source repository URL." };
		var homepageOption = new Option<string?>("--homepage") { Description = "The project homepage URL." };
		var licenseOption = new Option<string?>("--license")
			{ Description = "An SPDX license identifier. Defaults to MIT." };
		var projectNameOption = new Option<string?>("--project-name")
			{ Description = "The C# project name. Derived from --name when omitted." };
		var outputOption = new Option<string?>("--output")
			{ Description = "Directory to scaffold the project into. Defaults to './<project name>'." };
		var platformOption = new Option<string[]?>("--platform")
		{
			Description = "A target runtime identifier. Repeat for each platform.",
			Arity = ArgumentArity.OneOrMore,
			AllowMultipleArgumentsPerToken = true,
			HelpName = "rid"
		};
		var yesOption = new Option<bool>("--yes", "-y")
			{ Description = "Never prompt; accept every value the wizard would have offered as a default." };
		var nonInteractiveOption = new Option<bool>("--non-interactive")
			{ Description = "Never prompt; fail if a required value was not supplied, rather than defaulting it." };
		var templateVersionOption = new Option<string?>("--template-version")
			{ Description = "Install this exact plugin project template version instead of the latest prerelease." };
		var skipTemplateInstallOption = new Option<bool>("--skip-template-install")
			{ Description = "Never probe or install the plugin project template; it must already be installed." };
		var noRestoreOption = new Option<bool>("--no-restore")
			{ Description = "Skip the automatic NuGet restore 'dotnet new' would otherwise run." };

		var command = new Command("new", "Scaffold a new Macro Deck plugin project.");
		command.Add(nameOption);
		command.Add(idOption);
		command.Add(publisherOption);
		command.Add(descriptionOption);
		command.Add(repositoryOption);
		command.Add(homepageOption);
		command.Add(licenseOption);
		command.Add(projectNameOption);
		command.Add(outputOption);
		command.Add(platformOption);
		command.Add(yesOption);
		command.Add(nonInteractiveOption);
		command.Add(templateVersionOption);
		command.Add(skipTemplateInstallOption);
		command.Add(noRestoreOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var promptReader = prompt ?? CliPromptReader.Console;

			var inputs = new PluginScaffoldInputs
			{
				Name = parseResult.GetValue(nameOption),
				Id = parseResult.GetValue(idOption),
				Publisher = parseResult.GetValue(publisherOption),
				Description = parseResult.GetValue(descriptionOption),
				Repository = parseResult.GetValue(repositoryOption),
				Homepage = parseResult.GetValue(homepageOption),
				License = parseResult.GetValue(licenseOption),
				ProjectName = parseResult.GetValue(projectNameOption),
				Output = parseResult.GetValue(outputOption),
				Platforms = parseResult.GetValue(platformOption),
				Yes = parseResult.GetValue(yesOption),
				NonInteractive = parseResult.GetValue(nonInteractiveOption),
				TemplateVersion = parseResult.GetValue(templateVersionOption),
				SkipTemplateInstall = parseResult.GetValue(skipTemplateInstallOption),
				NoRestore = parseResult.GetValue(noRestoreOption)
			};

			var interactive = !inputs.Yes && !inputs.NonInteractive && promptReader.IsInteractive;

			var wizard = new PluginScaffoldWizard(promptReader, console);
			var outcome = wizard.Run(inputs, interactive, cancellationToken);

			if (outcome.Declined)
			{
				return ExitCode.Cancelled;
			}

			if (outcome.Error is { } error)
			{
				console.WriteError(error.Code, error.Message);
				return ExitCode.UsageError;
			}

			var generator = scaffoldGenerator ?? new DotnetNewGenerator();
			var result = await PluginScaffolder.ScaffoldAsync(outcome.Request!, generator, cancellationToken)
				.ConfigureAwait(false);

			return PluginScaffoldReporter.Report(console, result);
		});

		return command;
	}
}
