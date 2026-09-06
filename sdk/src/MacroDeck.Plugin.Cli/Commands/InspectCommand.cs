using System.CommandLine;
using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin inspect</c>: what <c>POST /api/plugin-installation/inspect</c> reports, without a
/// host - structural limits and the manifest through <see cref="IPluginArtifactReader.Inspect" />, then
/// entrypoints, permissions, dependencies, conflicts, icon packs, compatibility and signature shape read
/// straight off the resulting <see cref="PluginManifest" />. Writes nothing outside a temporary directory
/// it always deletes, and never writes at all unless <c>--directory</c> already exists on disk.
/// </summary>
internal static class InspectCommand
{
	public static Command Create()
	{
		var artifactOption = new Option<string?>("--artifact") { Description = "Path to a .macroDeckPlugin artifact." };
		var directoryOption = new Option<string?>("--directory")
			{ Description = "A version directory containing manifest.json." };
		var showDigestOption = new Option<bool>("--show-digest")
		{
			Description = "Also print the manifest's signable digest, base64-encoded - the exact bytes a " +
				"signature is computed over."
		};
		var outputFormatOption = GlobalOptions.CreateOutputFormatOption();

		var command = new Command("inspect", "Report what installing an artifact or version directory would find.");
		command.Add(artifactOption);
		command.Add(directoryOption);
		command.Add(showDigestOption);
		command.Add(outputFormatOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);
			var artifactPath = parseResult.GetValue(artifactOption);
			var directoryPath = parseResult.GetValue(directoryOption);
			var showDigest = parseResult.GetValue(showDigestOption);
			var outputFormat = parseResult.GetValue(outputFormatOption);

			if (artifactPath is null && directoryPath is null)
			{
				console.WriteError("no-selector",
					"Specify one of --artifact or --directory. Unlike validate and pack, inspect has no default input.");
				return ExitCode.UsageError;
			}

			if (artifactPath is not null && directoryPath is not null)
			{
				console.WriteError("too-many-selectors", "Specify only one of --artifact or --directory, not both.");
				return ExitCode.UsageError;
			}

			return artifactPath is not null
				? await InspectArtifactAsync(console, artifactPath, showDigest, outputFormat, cancellationToken)
					.ConfigureAwait(false)
				: await InspectDirectoryAsync(console, directoryPath!, showDigest, outputFormat, cancellationToken)
					.ConfigureAwait(false);
		});

		return command;
	}

	private static async Task<int> InspectArtifactAsync(CliConsole console,
		string artifactPath,
		bool showDigest,
		CliOutputFormat outputFormat,
		CancellationToken cancellationToken)
	{
		if (!File.Exists(artifactPath))
		{
			console.WriteError("artifact-not-found", $"No artifact at '{CliText.DisplayPath(artifactPath)}'.");
			return ExitCode.InputUnreadable;
		}

		var fileInfo = new FileInfo(artifactPath);
		var inspection = await ArtifactReaders.ArtifactReader.Inspect(artifactPath, cancellationToken)
			.ConfigureAwait(false);

		if (!inspection.Success || inspection.Manifest is not { } manifest)
		{
			var error = inspection.Error ?? PluginInstallError.Failed;
			var diagnostic = ArtifactErrorText.Describe(artifactPath, error, inspection.ErrorMessage);
			console.WriteError(diagnostic.Code, diagnostic.Message);
			return PluginInstallErrorExitCode.For(error);
		}

		// Reopens the archive read-only, after IPluginArtifactReader.Inspect has already accepted it -
		// see ArtifactEntryNames's own remarks on why that is safe. An empty set (read failure) means
		// "could not check", so From produces no false "missing" warnings.
		var presentEntryNames = await ArtifactEntryNames.ReadAsync(artifactPath, cancellationToken)
			.ConfigureAwait(false);

		var report = PluginInspectionReport.From(manifest,
			inspection.EntryCount,
			inspection.TotalUncompressedBytes,
			fileInfo.Length,
			presentEntryNames.Count > 0 ? presentEntryNames : null,
			"artifact");

		foreach (var warning in report.Warnings)
		{
			console.WriteWarning(warning.Code, warning.Message);
		}

		InspectionReportWriter.Write(console, outputFormat, report, showDigest);
		return ExitCode.Success;
	}

	private static Task<int> InspectDirectoryAsync(CliConsole console,
		string versionDirectory,
		bool showDigest,
		CliOutputFormat outputFormat,
		CancellationToken cancellationToken)
	{
		var manifestPath = Path.Combine(versionDirectory, PluginArtifactFiles.ManifestFileName);
		if (!File.Exists(manifestPath))
		{
			console.WriteError("manifest-not-found", $"No manifest at '{CliText.DisplayPath(manifestPath)}'.");
			return Task.FromResult(ExitCode.InputUnreadable);
		}

		var probeId = ProbeDirectoryPluginId(versionDirectory);
		var readResult = ArtifactReaders.ManifestReader.Read(manifestPath, probeId.Id, probeId.Version);

		if (!readResult.Success)
		{
			var error = readResult.Error!.Value;
			console.WriteError(CliText.KebabCase(error.ToString()),
				readResult.ErrorMessage ?? "The manifest is invalid.");
			return Task.FromResult(PluginManifestErrorExitCode.For(error));
		}

		var manifest = readResult.Manifest!;
		var files = Directory.EnumerateFiles(versionDirectory, "*", SearchOption.AllDirectories).ToList();
		var entryCount = Directory.EnumerateFileSystemEntries(versionDirectory, "*", SearchOption.AllDirectories)
			.Count();
		var totalBytes = files.Sum(path => new FileInfo(path).Length);

		var presentEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var path in files)
		{
			var relativePath = Path.GetRelativePath(versionDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
			presentEntryNames.Add(EntrypointPresence.Normalize(relativePath));
		}

		var report = PluginInspectionReport.From(manifest,
			entryCount,
			totalBytes,
			archiveBytes: null,
			presentEntryNames,
			"directory");

		foreach (var warning in report.Warnings)
		{
			console.WriteWarning(warning.Code, warning.Message);
		}

		InspectionReportWriter.Write(console, outputFormat, report, showDigest);
		return Task.FromResult(ExitCode.Success);
	}

	/// <summary>A version directory has no enclosing <c>&lt;id&gt;/versions/&lt;version&gt;</c> structure
	/// to check the manifest against - the same "check the manifest against itself" choice
	/// <see cref="ManifestValidator" /> makes, reading straight off the manifest's own declared id and
	/// version rather than requiring the directory name to carry them.</summary>
	private static (string Id, string Version) ProbeDirectoryPluginId(string versionDirectory)
	{
		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(
				Path.Combine(versionDirectory, PluginArtifactFiles.ManifestFileName)));

			var id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
			var version = document.RootElement.TryGetProperty("version", out var versionElement)
				? versionElement.GetString()
				: null;

			return (id ?? string.Empty, version ?? string.Empty);
		}
		catch (Exception ex) when (ex is IOException or JsonException)
		{
			return (string.Empty, string.Empty);
		}
	}
}
