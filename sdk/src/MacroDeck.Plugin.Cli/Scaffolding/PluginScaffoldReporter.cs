using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>Turns a <see cref="PluginScaffoldResult" /> into console output and an exit code - split out of
/// <c>Commands/NewCommand.cs</c> so the mapping is directly testable, matching
/// <c>Packing/PluginPackReporter.cs</c>.</summary>
internal static class PluginScaffoldReporter
{
	public static int Report(CliConsole console, PluginScaffoldResult result)
	{
		if (!result.Success)
		{
			console.WriteError(PluginScaffoldFailureCode.For(result.FailureReason),
				result.FailureMessage ?? "Scaffolding the plugin failed.",
				result.FailureDetail);
			return PluginScaffoldFailureExitCode.For(result.FailureReason);
		}

		console.Info($"Created plugin project at '{CliText.DisplayPath(result.OutputPath!)}'.");
		console.Info($"Manifest: {CliText.DisplayPath(result.ManifestPath!)}");
		console.Info($"Build configuration: {CliText.DisplayPath(result.BuildConfigPath!)}");

		foreach (var warning in RemainingPublicationGaps(result.ManifestPath!))
		{
			console.WriteWarning(warning.Code, warning.Message);
		}

		return ExitCode.Success;
	}

	/// <summary>What is still missing for the plugin `new` just scaffolded to be publishable, read back from
	/// the manifest it wrote rather than kept from the scaffolding request - so this reports on the exact
	/// document a plugin author will go on to edit, not on what was asked for. Empty on any read failure:
	/// this is narration on top of an already-successful scaffold, never something that should itself fail
	/// it.</summary>
	private static IReadOnlyList<CliDiagnostic> RemainingPublicationGaps(string manifestPath)
	{
		try
		{
			var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),
				PluginManifestJson.Options);

			return manifest is null ? [] : RequirementProblems.PublicationWarnings(manifest);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return [];
		}
	}
}
