using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Merging;

internal static class PluginMerger
{
	private static readonly JsonSerializerOptions _manifestWriteOptions =
		new(PluginManifestJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	public static async Task<PluginMergeResult> MergeAsync(IReadOnlyList<string> artifactPaths,
		string outputDirectory,
		bool force,
		string? stagingRoot = null,
		CancellationToken cancellationToken = default)
	{
		var workDirectory = BuildStaging.Create(stagingRoot);

		try
		{
			var parts = new List<MergePart>(artifactPaths.Count);

			for (var index = 0; index < artifactPaths.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var read = await ReadAsync(artifactPaths[index],
						Path.Combine(workDirectory, "artifacts", index.ToString(CultureInfo.InvariantCulture)),
						cancellationToken)
					.ConfigureAwait(false);

				if (read.Failure is { } failure)
				{
					return failure;
				}

				parts.Add(read.Part!);
			}

			var first = parts[0];

			foreach (var part in parts.Skip(1))
			{
				if (part.Manifest.Id != first.Manifest.Id || part.Manifest.Version != first.Manifest.Version)
				{
					return PluginMergeResult.Fail(PluginMergeFailureReason.IdentityMismatch,
						$"'{Display(first)}' is {first.Manifest.Id} {first.Manifest.Version}, but '{Display(part)}' " +
						$"is {part.Manifest.Id} {part.Manifest.Version}. Only builds of the same plugin version merge.");
				}

				if (!ComparableManifest(part.Manifest).AsSpan().SequenceEqual(ComparableManifest(first.Manifest)))
				{
					return PluginMergeResult.Fail(PluginMergeFailureReason.ManifestMismatch,
						$"The manifests of '{Display(first)}' and '{Display(part)}' differ in more than their " +
						"entrypoints. Build every runtime identifier from the same commit.");
				}
			}

			var entrypoints =
				new SortedDictionary<string, (PluginEntrypoint Entrypoint, MergePart Part)>(StringComparer.Ordinal);
			var files =
				new Dictionary<string, (PluginFileDigest Digest, MergePart Part)>(StringComparer.OrdinalIgnoreCase);

			foreach (var part in parts)
			{
				foreach (var (rid, entrypoint) in part.Manifest.Entrypoints)
				{
					if (entrypoints.TryGetValue(rid, out var existing))
					{
						return PluginMergeResult.Fail(PluginMergeFailureReason.DuplicateRid,
							$"Both '{Display(existing.Part)}' and '{Display(part)}' contain '{rid}'.");
					}

					entrypoints[rid] = (entrypoint, part);
				}

				foreach (var digest in part.Manifest.Files!)
				{
					if (!files.TryGetValue(digest.Path, out var existing))
					{
						files[digest.Path] = (digest, part);
						continue;
					}

					if (existing.Digest.Path != digest.Path || existing.Digest.Sha256 != digest.Sha256)
					{
						return PluginMergeResult.Fail(PluginMergeFailureReason.FileConflict,
							$"'{digest.Path}' differs between '{Display(existing.Part)}' and '{Display(part)}'. " +
							"Files outside a runtime identifier's own directory must be identical in every build.");
					}
				}
			}

			var outputPath = Path.Combine(outputDirectory,
				$"{first.Manifest.Id}-{first.Manifest.Version}{PluginArtifactFiles.MacroDeckPluginExtension}");

			if (File.Exists(outputPath) && !force)
			{
				return PluginMergeResult.Fail(PluginMergeFailureReason.OutputExists,
					$"'{CliText.DisplayPath(outputPath)}' already exists. Pass --force to overwrite it.");
			}

			var payload = Path.Combine(workDirectory, "payload");
			var manifestPath = Path.Combine(payload, PluginArtifactFiles.ManifestFileName);

			try
			{
				foreach (var (digest, part) in files.Values)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var destination = Path.Combine(payload, digest.Path.Replace('/', Path.DirectorySeparatorChar));
					Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
					File.Copy(part.FilePath(digest.Path), destination);
				}

				var merged = first.Manifest with
				{
					Files = null,
					Signature = null,
					Entrypoints = entrypoints.ToDictionary(pair => pair.Key,
						pair => pair.Value.Entrypoint,
						StringComparer.Ordinal)
				};

				await File.WriteAllBytesAsync(manifestPath,
						JsonSerializer.SerializeToUtf8Bytes(merged, _manifestWriteOptions),
						cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return PluginMergeResult.Fail(PluginMergeFailureReason.StagingFailed, ex.Message);
			}

			var pack = await PluginPacker.PackAsync(payload, manifestPath, _ => outputPath, force, cancellationToken)
				.ConfigureAwait(false);

			return PluginMergeResult.Packed([.. entrypoints.Keys], pack);
		}
		finally
		{
			BuildStaging.Delete(workDirectory);
		}
	}

	private static async Task<(MergePart? Part, PluginMergeResult? Failure)> ReadAsync(string artifactPath,
		string directory,
		CancellationToken cancellationToken)
	{
		if (!File.Exists(artifactPath))
		{
			return (null, PluginMergeResult.Rejected(artifactPath,
				PluginInstallError.ArtifactNotFound,
				"No artifact at this path."));
		}

		var inspection = await ArtifactReaders.ArtifactReader.Inspect(artifactPath, cancellationToken)
			.ConfigureAwait(false);

		if (!inspection.Success || inspection.Manifest is not { } manifest)
		{
			return (null, PluginMergeResult.Rejected(artifactPath,
				inspection.Error ?? PluginInstallError.Failed,
				inspection.ErrorMessage));
		}

		var extraction = await ArtifactReaders.ArtifactReader.ExtractTo(artifactPath, directory, cancellationToken)
			.ConfigureAwait(false);

		if (!extraction.Success)
		{
			return (null, PluginMergeResult.Rejected(artifactPath,
				extraction.Error ?? PluginInstallError.Failed,
				extraction.ErrorMessage));
		}

		var part = new MergePart(artifactPath, directory, manifest);

		// Per-runtime packages travel between CI jobs, so their bytes are checked against their own manifest.
		if (manifest.Files is not { } declared)
		{
			return (null, PluginMergeResult.Rejected(artifactPath, PluginInstallError.HashMismatch,
				"The manifest lists no files, so the package contents cannot be verified."));
		}

		var extracted = extraction.ExtractedFiles
			.Where(path => !string.Equals(path, PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase))
			.ToHashSet(StringComparer.Ordinal);

		foreach (var digest in declared)
		{
			if (!extracted.Remove(digest.Path) || Sha256(part.FilePath(digest.Path)) != digest.Sha256)
			{
				return (null, PluginMergeResult.Rejected(artifactPath, PluginInstallError.HashMismatch,
					$"'{digest.Path}' is missing or does not match the manifest."));
			}
		}

		if (extracted.Count > 0)
		{
			return (null, PluginMergeResult.Rejected(artifactPath, PluginInstallError.HashMismatch,
				$"'{extracted.Order(StringComparer.Ordinal).First()}' is not listed in the manifest."));
		}

		return (part, null);
	}

	private static byte[] ComparableManifest(PluginManifest manifest)
		=> JsonSerializer.SerializeToUtf8Bytes(
			manifest with { Files = null, Signature = null, Entrypoints = new Dictionary<string, PluginEntrypoint>() },
			_manifestWriteOptions);

	private static string Sha256(string path)
	{
		using var stream = File.OpenRead(path);
		return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static string Display(MergePart part) => CliText.DisplayPath(part.ArtifactPath);

	private sealed record MergePart(string ArtifactPath, string Directory, PluginManifest Manifest)
	{
		public string FilePath(string relativePath)
			=> Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}
}
