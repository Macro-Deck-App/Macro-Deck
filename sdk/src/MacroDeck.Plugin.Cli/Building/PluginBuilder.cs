using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Cli.Processes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Building;

/// <summary>
/// Turns a plugin project into a distributable artifact: builds every runtime identifier the manifest
/// declares, stages their outputs so no two can overwrite each other, and hands the result to
/// <see cref="PluginPacker" />. Nothing about manifest validation, file hashing, archive safety or artifact
/// writing lives here - <c>build</c> orchestrates, <c>pack</c> packages, and the two can never disagree
/// because there is only one implementation of the second.
/// </summary>
internal static class PluginBuilder
{
	/// <inheritdoc cref="PluginPacker" />
	private static readonly JsonSerializerOptions _stagedManifestWriteOptions =
		new(PluginManifestJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	public static async Task<PluginBuildResult> BuildAsync(PluginBuildRequest request,
		IPluginBuildRunner runner,
		Action<string>? onProgress = null,
		CancellationToken cancellationToken = default)
	{
		if (!Directory.Exists(request.SourceDirectory))
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.SourceNotFound,
				$"No source directory at '{CliText.DisplayPath(request.SourceDirectory)}'.");
		}

		var manifestRead = ReadManifest(request.ManifestPath);
		if (manifestRead.Failure is { } manifestFailure)
		{
			return manifestFailure;
		}

		var manifest = manifestRead.Manifest!;

		if (manifest.Entrypoints.Count == 0)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.NoEntrypointsDeclared,
				$"'{CliText.DisplayPath(request.ManifestPath)}' declares no entrypoints, so there is nothing to build.");
		}

		if (request.Rid is { } requestedRid && !manifest.Entrypoints.ContainsKey(requestedRid))
		{
			var declared = string.Join(", ", manifest.Entrypoints.Keys.OrderBy(rid => rid, StringComparer.Ordinal));

			return PluginBuildResult.Fail(PluginBuildFailureReason.RidNotDeclared,
				$"The manifest does not declare '{requestedRid}'. Declared runtime identifiers: {declared}.");
		}

		var sourceRoot = Path.GetFullPath(request.SourceDirectory);
		var configRead = PluginBuildConfigReader.Read(request.BuildConfigPath, sourceRoot);

		if (configRead.FailureReason is { } configFailure)
		{
			return PluginBuildResult.Fail(configFailure, configRead.FailureMessage!);
		}

		var targets = configRead.Targets!;

		var selectedRids = request.Rid is { } rid
			? [rid]
			: manifest.Entrypoints.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();

		var warnings = new List<CliDiagnostic>();

		foreach (var unselected in manifest.Entrypoints.Keys
			.Where(key => !selectedRids.Contains(key, StringComparer.Ordinal))
			.Where(key => !targets.ContainsKey(key))
			.OrderBy(key => key, StringComparer.Ordinal))
		{
			warnings.Add(new CliDiagnostic("target-not-configured",
				$"The manifest declares '{unselected}', which has no target in " +
				$"'{PluginBuildConfigReader.FileName}'. It was not requested by this build."));
		}

		foreach (var selected in selectedRids.Where(selected => !targets.ContainsKey(selected)))
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.TargetNotConfigured,
				$"The manifest declares '{selected}', but '{PluginBuildConfigReader.FileName}' has no target " +
				"for it. A declared runtime identifier is never silently skipped.",
				warnings: warnings);
		}

		var destinations = ResolveStagingDestinations(manifest, selectedRids, out var layoutFailure);
		if (layoutFailure is not null)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.EntrypointLayoutInvalid,
				layoutFailure,
				warnings: warnings);
		}

		if (DuplicateOutput(targets, selectedRids, sourceRoot) is { } duplicate)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.BuildConfigInvalid, duplicate, warnings: warnings);
		}

		// Checked before anything is built: a package that already exists, or a manifest that can never
		// pack, must not first cost a multi-platform publish.
		var outputPath = Path.Combine(request.OutputDirectory, ArtifactFileName(manifest, request.Rid));

		if (File.Exists(outputPath) && !request.Force)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.OutputExists,
				$"'{CliText.DisplayPath(outputPath)}' already exists. Pass --force to overwrite it.",
				warnings: warnings);
		}

		if (SchemaProblems(request.ManifestPath) is { Count: > 0 } schemaProblems)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.ManifestInvalid,
				$"'{CliText.DisplayPath(request.ManifestPath)}' does not match the plugin manifest schema.",
				string.Join(Environment.NewLine,
					schemaProblems.Select(problem => $"  {problem.Code}: {problem.Message}")),
				warnings);
		}

		var stagingDirectory = BuildStaging.Create(request.StagingRoot);

		try
		{
			foreach (var selected in selectedRids)
			{
				cancellationToken.ThrowIfCancellationRequested();
				onProgress?.Invoke(selected);

				var failure = await RunTargetAsync(targets[selected], sourceRoot, runner, warnings, cancellationToken)
					.ConfigureAwait(false);

				if (failure is not null)
				{
					return failure;
				}
			}

			// Shared assets first, then each runtime identifier's output on top: a stale 'runtimes/' left in
			// the project tree must never overwrite what this build just produced.
			try
			{
				var excluded = targets.Values
					.Select(target => Path.GetFullPath(Path.Combine(sourceRoot, target.Output)))
					.ToList();

				BuildStaging.CopySharedAssets(sourceRoot, stagingDirectory, excluded, cancellationToken);

				foreach (var selected in selectedRids)
				{
					BuildStaging.CopyTargetOutput(Path.Combine(sourceRoot, targets[selected].Output),
						Path.Combine(stagingDirectory,
							destinations[selected].Replace('/', Path.DirectorySeparatorChar)),
						cancellationToken);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return PluginBuildResult.Fail(PluginBuildFailureReason.StagingFailed, ex.Message, warnings: warnings);
			}

			var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(request.ManifestPath)) ?? sourceRoot;

			// Discovered here, from the project tree, because the staging directory pack() sees next holds
			// only build output - a compiled catalog, never the .resx it came from.
			var languages = PluginLanguages.Discover(manifestDirectory);

			warnings.AddRange(PluginLanguages.RecomputedWarnings(manifest.Languages, languages));

			var stagedManifest = StageManifest(manifest, selectedRids, request.Rid is not null, languages);
			var stagedManifestPath = Path.Combine(stagingDirectory, PluginArtifactFiles.ManifestFileName);

			try
			{
				await File.WriteAllBytesAsync(stagedManifestPath,
						JsonSerializer.SerializeToUtf8Bytes(stagedManifest, _stagedManifestWriteOptions),
						cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return PluginBuildResult.Fail(PluginBuildFailureReason.StagingFailed, ex.Message, warnings: warnings);
			}

			var missing = EntrypointPresence.Missing(stagedManifest,
				BuildStaging.EntryNames(stagingDirectory),
				"build output");

			if (missing.Count > 0)
			{
				return PluginBuildResult.Fail(PluginBuildFailureReason.EntrypointMissing,
					"The build finished, but a requested runtime identifier did not produce the entrypoint its " +
					"manifest declares.",
					string.Join(Environment.NewLine, missing.Select(problem => problem.Message)),
					warnings);
			}

			// Readiness is evaluated on the staged manifest, so a targeted --rid build never warns about
			// entrypoints its own narrowing deliberately removed; the generated-field check runs against the
			// source manifest instead, since staging always strips 'files'/'signature' and the staged copy
			// would never show a hand-authored one.
			warnings.AddRange(RequirementProblems.PublicationWarnings(stagedManifest));
			warnings.AddRange(RequirementProblems.GeneratedFieldWarnings(manifest,
				ManifestValidator.HasProjectFile(manifestDirectory)));

			var pack = await PluginPacker.PackAsync(stagingDirectory,
					stagedManifestPath,
					_ => outputPath,
					request.Force,
					cancellationToken)
				.ConfigureAwait(false);

			return PluginBuildResult.Packed(selectedRids, pack, warnings);
		}
		finally
		{
			BuildStaging.Delete(stagingDirectory);
		}
	}

	private static string ArtifactFileName(PluginManifest manifest, string? rid)
	{
		// A targeted build declares only the runtime identifier it built, so its artifact is a different
		// package from a full build's and must never overwrite one.
		var suffix = rid is null ? string.Empty : $"-{rid}";

		return $"{manifest.Id}-{manifest.Version}{suffix}{PluginArtifactFiles.MacroDeckPluginExtension}";
	}

	/// <summary>Two selected targets writing into the same directory - or one into a parent of another -
	/// would make each runtime identifier's staged payload depend on build order.</summary>
	private static string? DuplicateOutput(IReadOnlyDictionary<string, PluginBuildTarget> targets,
		IReadOnlyList<string> selectedRids,
		string sourceRoot)
	{
		var resolved = selectedRids
			.Select(rid => (Rid: rid, Path: Path.GetFullPath(Path.Combine(sourceRoot, targets[rid].Output))))
			.ToList();

		foreach (var (rid, path) in resolved)
		{
			foreach (var (otherRid, otherPath) in resolved.Where(other =>
				!other.Rid.Equals(rid, StringComparison.Ordinal)))
			{
				if (path.Equals(otherPath, StringComparison.Ordinal))
				{
					return $"Targets '{rid}' and '{otherRid}' share the output directory " +
						$"'{targets[rid].Output}'. Each runtime identifier needs its own.";
				}

				if (otherPath.StartsWith(
					path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar,
					StringComparison.Ordinal))
				{
					return $"Target '{otherRid}' writes inside target '{rid}'s output directory " +
						$"'{targets[rid].Output}'. Each runtime identifier needs its own.";
				}
			}
		}

		return null;
	}

	/// <summary>The published schema alone - the same embedded document <c>validate</c> evaluates, and one
	/// of the layers the packer's own validation runs. Deliberately not the whole validator, which
	/// existence-checks the host's entrypoint and so cannot pass before a build; this only rules out a
	/// manifest that could never pack, before a multi-platform publish is spent on it.</summary>
	private static List<ManifestProblem> SchemaProblems(string manifestPath)
	{
		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

			return PluginManifestSchema.Validate(document.RootElement)
				.Where(problem => problem.Severity == ManifestProblemSeverity.Error)
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return [];
		}
	}

	private static async Task<PluginBuildResult?> RunTargetAsync(PluginBuildTarget target,
		string sourceRoot,
		IPluginBuildRunner runner,
		IReadOnlyList<CliDiagnostic> warnings,
		CancellationToken cancellationToken)
	{
		var workingDirectory = target.WorkingDirectory is null
			? sourceRoot
			: Path.GetFullPath(Path.Combine(sourceRoot, target.WorkingDirectory));

		PluginBuildRunResult run;
		try
		{
			run = await runner.RunAsync(target.Executable, target.Arguments, workingDirectory, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (ProcessLaunchException ex)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.BuildToolNotFound,
				$"Could not launch '{ex.FileName}' to build '{target.Rid}'. Install it and ensure it is on PATH, " +
				"or build this runtime identifier on a machine whose toolchain supports it.",
				warnings: warnings);
		}

		if (!run.Success)
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.BuildFailed,
				$"Building '{target.Rid}' failed: '{target.Executable}' exited with {run.ExitCode}.",
				run.CombinedOutput,
				warnings);
		}

		var output = Path.GetFullPath(Path.Combine(sourceRoot, target.Output));

		if (!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any())
		{
			return PluginBuildResult.Fail(PluginBuildFailureReason.TargetOutputMissing,
				$"Building '{target.Rid}' succeeded, but its declared output '{target.Output}' is missing or empty.",
				warnings: warnings);
		}

		return null;
	}

	/// <summary>Where each runtime identifier's build output is staged: the directory its declared
	/// entrypoint lives in, so the manifest and the payload can never disagree about the layout. Scaffolded
	/// manifests put that at <c>runtimes/&lt;rid&gt;/</c>, which is what keeps a macOS and a Linux build -
	/// identically named executables - from colliding. Thin wrapper over <see cref="EntrypointLayout" />,
	/// which <see cref="ManifestValidator" /> shares, so the two commands can never disagree about the same
	/// manifest.</summary>
	private static Dictionary<string, string> ResolveStagingDestinations(PluginManifest manifest,
		IReadOnlyList<string> selectedRids,
		out string? failure)
	{
		var resolution = EntrypointLayout.Resolve(manifest, selectedRids);
		failure = resolution.Failure;
		return new Dictionary<string, string>(resolution.Destinations, StringComparer.Ordinal);
	}

	/// <summary>The manifest written into the payload. <c>files</c> and <c>signature</c> are always dropped:
	/// the packer recomputes <c>files</c> from the staged bytes, and a signature carried over from the
	/// project would describe a digest that no longer exists - <c>build</c> produces an unsigned package by
	/// design (#617). A targeted build additionally narrows <c>entrypoints</c> to what it actually built, so
	/// the artifact is an honest single-platform package rather than one claiming platforms it never
	/// produced.</summary>
	private static PluginManifest StageManifest(PluginManifest manifest,
		IReadOnlyList<string> selectedRids,
		bool narrowEntrypoints,
		IReadOnlyList<string> languages)
	{
		var staged = manifest with
		{
			Files = null,
			Signature = null,
			Languages = languages.Count > 0 ? languages : manifest.Languages
		};

		if (!narrowEntrypoints)
		{
			return staged;
		}

		return staged with
		{
			Entrypoints = selectedRids.ToDictionary(rid => rid,
				rid => manifest.Entrypoints[rid],
				StringComparer.Ordinal)
		};
	}

	private static (PluginManifest? Manifest, PluginBuildResult? Failure) ReadManifest(string manifestPath)
	{
		if (!File.Exists(manifestPath))
		{
			return (null, PluginBuildResult.Fail(PluginBuildFailureReason.ManifestNotFound,
				$"No manifest at '{CliText.DisplayPath(manifestPath)}'."));
		}

		try
		{
			// Deliberately not ManifestValidator: it existence-checks the current host's entrypoint against
			// the manifest's own directory, which cannot pass before anything has been built. The staged
			// manifest goes through the full validator inside PluginPacker instead, so there is still exactly
			// one definition of "valid".
			var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),
				PluginManifestJson.Options);

			return manifest is null
				? (null, PluginBuildResult.Fail(PluginBuildFailureReason.ManifestMalformed,
					$"'{CliText.DisplayPath(manifestPath)}' is empty."))
				: (manifest, null);
		}
		catch (JsonException ex)
		{
			return (null, PluginBuildResult.Fail(PluginBuildFailureReason.ManifestMalformed,
				$"'{CliText.DisplayPath(manifestPath)}' could not be read as a manifest: {ex.Message}"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return (null, PluginBuildResult.Fail(PluginBuildFailureReason.ManifestNotFound,
				$"Could not read '{CliText.DisplayPath(manifestPath)}': {ex.Message}"));
		}
	}
}
