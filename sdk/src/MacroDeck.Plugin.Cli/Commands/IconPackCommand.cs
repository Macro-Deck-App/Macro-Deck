using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.IconPacks;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Commands;

internal static class IconPackCommand
{
	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

	public static Command Create()
	{
		var command = new Command("icon-pack",
			"Bundle icon packs with the plugin project: add, list or remove the packs its manifest declares.");
		command.Add(CreateAdd());
		command.Add(CreateList());
		command.Add(CreateRemove());
		return command;
	}

	private static Command CreateAdd()
	{
		var pathArgument = new Argument<string>("path") { Description = "The .macroDeckIconPack file to bundle." };
		var keyOption = new Option<string?>("--key")
		{
			Description = "The key the plugin addresses the pack by. Defaults to a slug of the pack name.",
			HelpName = "key"
		};
		var copyOption = new Option<bool>("--copy")
			{ Description = "Keep a pack that is already inside the project where it is, instead of moving it." };
		var forceOption = new Option<bool>("--force") { Description = "Replace a pack already bundled under the key." };
		var (sourceOption, manifestOption) = CreateProjectOptions();

		var command = new Command("add",
			$"Validate an icon pack, place it at {PluginBundledIconPacks.DefaultPath("<key>")} and declare it in the manifest.");
		command.Add(pathArgument);
		command.Add(keyOption);
		command.Add(copyOption);
		command.Add(forceOption);
		command.Add(sourceOption);
		command.Add(manifestOption);

		command.SetAction(parseResult =>
		{
			var console = ConsoleFactory.From(parseResult);
			var (source, manifestPath) = ResolveProject(parseResult, sourceOption, manifestOption);

			var outcome = BundledIconPackProject.Add(new BundledIconPackAddRequest
			{
				SourceDirectory = source,
				ManifestPath = manifestPath,
				PackPath = parseResult.GetValue(pathArgument)!,
				Key = parseResult.GetValue(keyOption),
				Copy = parseResult.GetValue(copyOption),
				Force = parseResult.GetValue(forceOption)
			});

			foreach (var warning in outcome.Warnings)
			{
				console.WriteWarning(warning.Code, warning.Message);
			}

			if (outcome.Error is { } error)
			{
				console.WriteError(error.Code, error.Message, outcome.ErrorDetail);
				return outcome.ExitCode;
			}

			if (outcome.MovedFrom is { } movedFrom)
			{
				console.Info($"Moved '{movedFrom}' to '{outcome.TargetPath}'. Pass --copy to keep the original in place.");
			}
			else if (outcome.Placement == BundledIconPackPlacement.Copied)
			{
				console.Info($"Copied the pack to '{outcome.TargetPath}'.");
			}

			console.Info((outcome.Replaced ? "Replaced" : "Added") +
				$" icon pack '{outcome.PackName}' ({outcome.IconCount} icon(s)) under the key '{outcome.Key}'.");
			return ExitCode.Success;
		});

		return command;
	}

	private static Command CreateList()
	{
		var outputFormatOption = GlobalOptions.CreateOutputFormatOption();
		var (sourceOption, manifestOption) = CreateProjectOptions();

		var command = new Command("list", "List the icon packs the manifest bundles.");
		command.Add(outputFormatOption);
		command.Add(sourceOption);
		command.Add(manifestOption);

		command.SetAction(parseResult =>
		{
			var console = ConsoleFactory.From(parseResult);
			var (source, manifestPath) = ResolveProject(parseResult, sourceOption, manifestOption);

			if (BundledIconPackProject.Load(source, manifestPath, out var project) is { Error: { } error } failure)
			{
				console.WriteError(error.Code, error.Message, failure.ErrorDetail);
				return failure.ExitCode;
			}

			var inspection = BundledIconPackInspection.InspectDirectory(project.Manifest, project.Root);

			if (parseResult.GetValue(outputFormatOption) == CliOutputFormat.Json)
			{
				var packs = new JsonArray();
				foreach (var pack in inspection.Packs)
				{
					packs.Add(new JsonObject
					{
						["key"] = pack.Key,
						["path"] = pack.Path,
						["present"] = pack.Present,
						["name"] = pack.Name,
						["iconCount"] = pack.IconCount,
						["problem"] = pack.Problem
					});
				}

				console.WriteLine(new JsonObject { ["bundledIconPacks"] = packs }.ToJsonString(_jsonOptions));
				return ExitCode.Success;
			}

			if (inspection.Packs.Count == 0)
			{
				console.WriteLine("No bundled icon packs declared.");
				return ExitCode.Success;
			}

			foreach (var pack in inspection.Packs)
			{
				console.WriteLine($"{pack.Key}: {pack.Path} - {DescribePack(pack)}");
			}

			return ExitCode.Success;
		});

		return command;
	}

	private static Command CreateRemove()
	{
		var keyArgument = new Argument<string>("key") { Description = "The key of the bundled pack to remove." };
		var (sourceOption, manifestOption) = CreateProjectOptions();

		var command = new Command("remove",
			"Remove a bundled icon pack from the manifest and delete its file from the project.");
		command.Add(keyArgument);
		command.Add(sourceOption);
		command.Add(manifestOption);

		command.SetAction(parseResult =>
		{
			var console = ConsoleFactory.From(parseResult);
			var (source, manifestPath) = ResolveProject(parseResult, sourceOption, manifestOption);

			var outcome = BundledIconPackProject.Remove(source, manifestPath, parseResult.GetValue(keyArgument)!);

			if (outcome.Error is { } error)
			{
				console.WriteError(error.Code, error.Message, outcome.ErrorDetail);
				return outcome.ExitCode;
			}

			console.Info(outcome.FileDeleted
				? $"Removed icon pack '{outcome.Key}' and deleted '{outcome.TargetPath}'."
				: $"Removed icon pack '{outcome.Key}'. '{outcome.TargetPath}' was not deleted, because it is not a " +
				"file inside the project.");
			return ExitCode.Success;
		});

		return command;
	}

	internal static string DescribePack(InspectedBundledIconPack pack)
	{
		if (!pack.Present)
		{
			return "missing";
		}

		if (pack.Problem is { } problem)
		{
			return $"unreadable ({problem})";
		}

		return $"{pack.Name ?? "(unnamed)"}, {pack.IconCount} icon(s)";
	}

	private static (Option<string> Source, Option<string?> Manifest) CreateProjectOptions()
		=> (new Option<string>("--source")
			{
				Description = "The plugin project directory. Pack paths are relative to it.",
				DefaultValueFactory = _ => "."
			},
			new Option<string?>("--manifest") { Description = "Path to manifest.json. Defaults to <source>/manifest.json." });

	private static (string Source, string ManifestPath) ResolveProject(ParseResult parseResult,
		Option<string> sourceOption,
		Option<string?> manifestOption)
	{
		var source = parseResult.GetValue(sourceOption) ?? ".";
		return (source, parseResult.GetValue(manifestOption) ?? Path.Combine(source, PluginArtifactFiles.ManifestFileName));
	}
}
