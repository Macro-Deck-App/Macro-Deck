using System.IO.Compression;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.IconPacks;

internal sealed record InspectedBundledIconPack
{
	public required string Key { get; init; }

	public required string Path { get; init; }

	public required bool Present { get; init; }

	public bool? ListedInFiles { get; init; }

	public string? Name { get; init; }

	public string? Version { get; init; }

	public int? IconCount { get; init; }

	public string? Problem { get; init; }
}

internal sealed record BundledIconPackInspectionResult
{
	public required IReadOnlyList<InspectedBundledIconPack> Packs { get; init; }

	public required IReadOnlyList<CliDiagnostic> Warnings { get; init; }
}

internal static class BundledIconPackInspection
{
	public static BundledIconPackInspectionResult InspectDirectory(PluginManifest manifest, string directory)
	{
		var root = Path.GetFullPath(directory);

		return Inspect(manifest,
			path =>
			{
				if (!IsContained(path, root))
				{
					return null;
				}

				var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
				return File.Exists(full) ? File.OpenRead(full) : null;
			},
			"directory",
			checkFiles: false);
	}

	public static BundledIconPackInspectionResult InspectArtifact(PluginManifest manifest, string artifactPath)
	{
		ZipArchive? archive = null;
		FileStream? stream = null;

		try
		{
			try
			{
				stream = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read);
				archive = new ZipArchive(stream, ZipArchiveMode.Read);
			}
			catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
			{
				return new BundledIconPackInspectionResult { Packs = [], Warnings = [] };
			}

			var entries = archive.Entries
				.GroupBy(entry => EntrypointPresence.Normalize(entry.FullName), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

			return Inspect(manifest,
				path =>
				{
					if (!entries.TryGetValue(EntrypointPresence.Normalize(path), out var entry) ||
						entry.Length > PluginArtifactLimits.MaxEntryBytes)
					{
						return null;
					}

					var buffer = new MemoryStream();
					using (var entryStream = entry.Open())
					{
						entryStream.CopyTo(buffer);
					}

					buffer.Position = 0;
					return buffer;
				},
				"artifact",
				checkFiles: true);
		}
		finally
		{
			archive?.Dispose();
			stream?.Dispose();
		}
	}

	private static BundledIconPackInspectionResult Inspect(PluginManifest manifest,
		Func<string, Stream?> open,
		string subjectNoun,
		bool checkFiles)
	{
		var packs = new List<InspectedBundledIconPack>();
		var warnings = new List<CliDiagnostic>();
		var listed = manifest.Files is { } files
			? new HashSet<string>(files.Select(file => EntrypointPresence.Normalize(file.Path)), StringComparer.OrdinalIgnoreCase)
			: null;

		foreach (var declared in manifest.BundledIconPacks ?? [])
		{
			bool? listedInFiles = checkFiles && listed is not null
				? listed.Contains(EntrypointPresence.Normalize(declared.Path))
				: null;

			if (listedInFiles == false)
			{
				warnings.Add(new CliDiagnostic("bundled-icon-pack-not-in-files",
					$"Bundled icon pack '{declared.Key}' declares '{declared.Path}', which is not listed in 'files'. " +
					"The host skips a pack the signed file list does not cover."));
			}

			IconPackArchiveReadResult? read;
			try
			{
				using var stream = open(declared.Path);
				read = stream is null ? null : IconPackArchive.Read(stream);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
			{
				read = new IconPackArchiveReadResult { Error = ex.Message };
			}

			if (read is null)
			{
				warnings.Add(new CliDiagnostic("bundled-icon-pack-missing",
					$"Bundled icon pack '{declared.Key}' declares '{declared.Path}', which is not in the {subjectNoun}."));
				packs.Add(new InspectedBundledIconPack
					{ Key = declared.Key, Path = declared.Path, Present = false, ListedInFiles = listedInFiles });
				continue;
			}

			if (read.Info is null)
			{
				warnings.Add(new CliDiagnostic("bundled-icon-pack-invalid",
					$"Bundled icon pack '{declared.Key}' at '{declared.Path}' cannot be read: {read.Error}"));
			}

			packs.Add(new InspectedBundledIconPack
			{
				Key = declared.Key,
				Path = declared.Path,
				Present = true,
				ListedInFiles = listedInFiles,
				Name = read.Info?.Name,
				Version = read.Info?.Version,
				IconCount = read.Info?.Icons.Count,
				Problem = read.Error
			});
		}

		return new BundledIconPackInspectionResult { Packs = packs, Warnings = warnings };
	}

	private static bool IsContained(string relativePath, string root)
	{
		if (Path.IsPathRooted(relativePath))
		{
			return false;
		}

		var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
		var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
		return resolved.StartsWith(prefix, StringComparison.Ordinal);
	}
}
