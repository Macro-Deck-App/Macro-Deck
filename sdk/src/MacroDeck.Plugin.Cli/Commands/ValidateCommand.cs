using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin validate</c>: runs <see cref="ManifestValidator" /> against a manifest, a version
/// directory, or a packed artifact, and reports every problem found.
/// </summary>
internal static class ValidateCommand
{
	public static Command Create()
	{
		var manifestOption = new Option<string?>("--manifest") { Description = "Path to a manifest.json file." };
		var artifactOption = new Option<string?>("--artifact") { Description = "Path to a .macroDeckPlugin artifact." };
		var directoryOption = new Option<string?>("--directory")
			{ Description = "A version directory containing manifest.json." };
		var outputFormatOption = GlobalOptions.CreateOutputFormatOption();
		var levelOption = CreateLevelOption();

		var command = new Command("validate", "Validate a plugin manifest, version directory, or artifact.");
		command.Add(manifestOption);
		command.Add(artifactOption);
		command.Add(directoryOption);
		command.Add(outputFormatOption);
		command.Add(levelOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var manifestPath = parseResult.GetValue(manifestOption);
			var artifactPath = parseResult.GetValue(artifactOption);
			var directoryPath = parseResult.GetValue(directoryOption);
			var outputFormat = parseResult.GetValue(outputFormatOption);
			var requestedLevel = parseResult.GetValue(levelOption);

			var selectorCount = CountSelectors(manifestPath, artifactPath, directoryPath);
			if (selectorCount > 1)
			{
				console.WriteError("too-many-selectors",
					"Specify at most one of --manifest, --artifact or --directory.");
				return ExitCode.UsageError;
			}

			// Implied by the selector when --level is absent: an artifact is something that was already
			// packed, so package readiness is the natural default; a loose manifest or version directory is
			// still being developed, so development is.
			var level = requestedLevel ??
				(artifactPath is not null
					? PluginManifestValidationLevel.Package
					: PluginManifestValidationLevel.Development);

			console.Trace($"Validating with selector count {selectorCount} at level {level}.");

			var result = artifactPath is not null
				? await ManifestValidator.ValidateArtifactAsync(artifactPath, level, cancellationToken)
					.ConfigureAwait(false)
				: directoryPath is not null
					? await ManifestValidator.ValidateDirectoryAsync(directoryPath, level, cancellationToken)
						.ConfigureAwait(false)
					: await ManifestValidator
						.ValidateManifestFileAsync(manifestPath ?? "manifest.json", level, cancellationToken)
						.ConfigureAwait(false);

			ValidationResultWriter.Write(console, outputFormat, result);
			return result.ExitCode;
		});

		return command;
	}

	private static int CountSelectors(params string?[] selectors) => selectors.Count(selector => selector is not null);

	/// <summary>Nullable rather than defaulted: <c>null</c> means "--level was not given", which the
	/// command's own action then implies from the selector - the parser cannot know that on its own. An
	/// unrecognized token is a usage error naming --level and its three legal tokens, reported through
	/// <see cref="ArgumentResult.AddError" /> exactly like every other closed-vocabulary option
	/// (<see cref="CliOptionParsing" />), so it maps to <see cref="ExitCode.UsageError" /> before the action
	/// ever runs - nothing about the manifest is judged for an unrecognized --level.</summary>
	private static Option<PluginManifestValidationLevel?> CreateLevelOption()
	{
		var option = new Option<PluginManifestValidationLevel?>("--level")
		{
			Description = "How strictly to validate: development, package or publication. Defaults from the " +
				"selector (development for --manifest/--directory, package for --artifact)."
		};

		option.CustomParser = result =>
		{
			if (result.Tokens.Count == 0)
			{
				return null;
			}

			var raw = result.Tokens[0].Value;

			return raw.ToLowerInvariant() switch
			{
				"development" => PluginManifestValidationLevel.Development,
				"package" => PluginManifestValidationLevel.Package,
				"publication" => PluginManifestValidationLevel.Publication,
				_ => Reject(result, raw)
			};
		};

		return option;
	}

	private static PluginManifestValidationLevel? Reject(ArgumentResult result, string raw)
	{
		result.AddError($"'{raw}' is not a recognized --level. Expected one of: development, package, publication.");
		return null;
	}
}

/// <summary>Renders a <see cref="ManifestValidationResult" /> as text or JSON - shared by <c>validate</c>
/// and <c>inspect</c>'s own validity summary so the two never format the same shape differently.</summary>
internal static class ValidationResultWriter
{
	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

	public static void Write(CliConsole console, CliOutputFormat format, ManifestValidationResult result)
	{
		if (format == CliOutputFormat.Json)
		{
			WriteJson(console, result);
			return;
		}

		WriteText(console, result);
	}

	private static void WriteText(CliConsole console, ManifestValidationResult result)
	{
		// This already renders the "error <code>: <message>" / "warning <code>: <message>" shape, but it is
		// the validation report on stdout - not the CliConsole.WriteError/WriteWarning error channel - so it
		// stays on console.WriteLine/stdout deliberately. Redirecting it to stderr would break piping the
		// report itself, e.g. `validate > report.txt`.
		foreach (var problem in result.Problems)
		{
			var isError = problem.Severity == ManifestProblemSeverity.Error;
			var tag = isError ? "error" : "warning";
			var line = $"{tag} {problem.Code}: {problem.Message}" +
				(problem.Pointer is null ? string.Empty : $" [{problem.Pointer}]") +
				(problem.Level is { } requiringLevel
					? $" ({requiringLevel.ToString().ToLowerInvariant()})"
					: string.Empty);

			console.WriteLine(console.Colorize(line, isError ? CliColor.Red : CliColor.Yellow));
		}

		var errorCount = result.Problems.Count(p => p.Severity == ManifestProblemSeverity.Error);
		var warningCount = result.Problems.Count(p => p.Severity == ManifestProblemSeverity.Warning);

		console.WriteLine();

		// Only a manifest the real reader actually produced makes "<id> <version>" a trustworthy heading -
		// an id that failed validation (e.g. "'NOT VALID ID!!' is not a valid plugin id") is exactly the
		// unreadable text issue #556 asks not to echo back as a heading. Everything else falls back to the
		// resolved path of whatever was validated.
		var subject = result.Manifest is not null
			? $"{result.PluginId} {result.Version}"
			: result.Subject ?? "(unknown)";

		console.WriteLine($"{subject}: {errorCount} error(s), {warningCount} warning(s).");
	}

	private static void WriteJson(CliConsole console, ManifestValidationResult result)
	{
		var problems = new JsonArray();

		foreach (var problem in result.Problems)
		{
			var node = new JsonObject
			{
				["severity"] = problem.Severity == ManifestProblemSeverity.Error ? "error" : "warning",
				["code"] = problem.Code,
				["message"] = problem.Message,
				["pointer"] = problem.Pointer
			};

			// Omitted, not null, when absent: unlike "level" below (always present, one meaning - the run's
			// own level), "requiredBy" only has something to say for a problem a level actually requires -
			// see RequirementProblems' own remarks on why a generated-field warning's Level is null.
			if (problem.Level is { } requiredBy)
			{
				node["requiredBy"] = requiredBy.ToString().ToLowerInvariant();
			}

			problems.Add(node);
		}

		var payload = new JsonObject
		{
			["valid"] = result.Valid,
			["pluginId"] = result.PluginId,
			["version"] = result.Version,
			["level"] = result.Level.ToString().ToLowerInvariant(),
			["problems"] = problems
		};

		console.WriteLine(payload.ToJsonString(_jsonOptions));
	}
}
