using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Building;

/// <summary>
/// Assembles the payload tree <c>build</c> hands to the packer. Files are copied one at a time rather than
/// moved or linked: <see cref="File.Copy(string, string)" /> dereferences symlinks, and the packer rejects
/// any symlink it finds, so a toolchain that emits symlinked native libraries would otherwise fail at pack
/// time with a message pointing at a temporary path.
/// </summary>
internal static class BuildStaging
{
	/// <summary>Directories never staged from the project tree, whatever the plugin contains: build
	/// scratch, version control, editor state, and dependency caches.</summary>
	private static readonly string[] _excludedDirectories =
		["bin", "obj", ".git", ".vs", ".idea", "node_modules"];

	private static readonly string[] _excludedFiles = [".DS_Store"];

	private static readonly string[] _sourceExtensions =
	[
		".csproj", ".fsproj", ".vbproj", ".sln", ".slnx", ".props", ".targets", ".user", ".cs", ".fs", ".fsx",
		".vb", ".resx"
	];

	public static string Create(string? stagingRoot)
	{
		var root = stagingRoot ?? Path.GetTempPath();
		var directory = Path.Combine(root, $"macrodeck-plugin-build-{Guid.NewGuid():N}");

		Directory.CreateDirectory(directory);

		return directory;
	}

	public static IReadOnlyList<string> CopyDeclared(string sourceRoot,
		string stagingRoot,
		IEnumerable<string> relativePaths,
		IReadOnlyCollection<string> excludedDirectories,
		CancellationToken cancellationToken)
	{
		var root = Path.GetFullPath(sourceRoot);
		var excluded = ExcludedSet(excludedDirectories);
		var skipped = new List<string>();

		foreach (var relativePath in relativePaths)
		{
			var source = Path.GetFullPath(Path.Combine(root, relativePath));

			if (excluded.Any(directory => source.Equals(directory, PathComparison) ||
				source.StartsWith(directory + Path.DirectorySeparatorChar, PathComparison)))
			{
				skipped.Add(relativePath);
				continue;
			}

			IEnumerable<string> files;

			if (File.Exists(source))
			{
				files = [source];
			}
			else if (Directory.Exists(source))
			{
				files = Walk(root, source, excluded, skipSources: false, cancellationToken);
			}
			else
			{
				throw new FileNotFoundException($"'{relativePath}' is declared for packaging but does not exist.");
			}

			foreach (var file in files)
			{
				var relative = Path.GetRelativePath(root, file);

				if (IsSource(relative))
				{
					skipped.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
					continue;
				}

				CopyFiles([file], root, stagingRoot);
			}
		}

		return skipped;
	}

	public static void CopyTargetOutput(string outputDirectory,
		string destinationDirectory,
		CancellationToken cancellationToken)
	{
		var root = Path.GetFullPath(outputDirectory);

		Directory.CreateDirectory(destinationDirectory);
		CopyFiles(Walk(root, root, new HashSet<string>(StringComparer.Ordinal), skipSources: false, cancellationToken),
			root,
			destinationDirectory);
	}

	public static IReadOnlyList<string> Unpackaged(string sourceRoot,
		string manifestPath,
		string stagingRoot,
		IReadOnlyCollection<string> excludedDirectories,
		CancellationToken cancellationToken)
	{
		var root = Path.GetFullPath(sourceRoot);
		var manifest = Path.GetFullPath(manifestPath);
		var scanned = Path.GetDirectoryName(manifest) ?? root;

		if (!scanned.Equals(root, PathComparison) &&
			!scanned.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison))
		{
			scanned = root;
		}

		return Walk(root, scanned, ExcludedSet(excludedDirectories), skipSources: true, cancellationToken)
			.Where(file => !file.Equals(manifest, PathComparison))
			.Select(file => Path.GetRelativePath(root, file))
			.Where(relative => !File.Exists(Path.Combine(stagingRoot, relative)))
			.Select(relative => relative.Replace(Path.DirectorySeparatorChar, '/'))
			.Order(StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>Best-effort: a cancelled build whose tool still holds a handle into the tree can leave it
	/// behind, and that must never mask the failure the caller is actually reporting.</summary>
	public static void Delete(string stagingDirectory)
	{
		try
		{
			if (Directory.Exists(stagingDirectory))
			{
				Directory.Delete(stagingDirectory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	/// <summary>The set of entry names present in the staged tree, normalized and compared the way the
	/// packer compares them, for <see cref="Manifests.EntrypointPresence" />.</summary>
	public static IReadOnlySet<string> EntryNames(string stagingRoot)
	{
		var root = Path.GetFullPath(stagingRoot);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			names.Add(Manifests.EntrypointPresence.Normalize(Path.GetRelativePath(root, file)));
		}

		return names;
	}

	private static void CopyFiles(IEnumerable<string> files, string root, string destinationRoot)
	{
		foreach (var file in files)
		{
			var destination = Path.Combine(destinationRoot, Path.GetRelativePath(root, file));

			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Copy(file, destination, overwrite: true);
		}
	}

	private static IEnumerable<string> Walk(string root,
		string current,
		IReadOnlySet<string> excluded,
		bool skipSources,
		CancellationToken cancellationToken)
	{
		foreach (var file in Directory.EnumerateFiles(current))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var name = Path.GetFileName(file);

			if (_excludedFiles.Contains(name, StringComparer.OrdinalIgnoreCase) ||
				PluginArtifactFiles.HasArtifactExtension(name) ||
				name.Equals(PluginBuildConfigReader.FileName, StringComparison.OrdinalIgnoreCase) ||
				(current.Equals(root, StringComparison.Ordinal) &&
					name.Equals(PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase)) ||
				(skipSources && IsSource(Path.GetRelativePath(root, file))))
			{
				continue;
			}

			yield return file;
		}

		foreach (var directory in Directory.EnumerateDirectories(current))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var name = Path.GetFileName(directory);

			if (_excludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase) ||
				excluded.Contains(Path.GetFullPath(directory)))
			{
				continue;
			}

			foreach (var file in Walk(root, directory, excluded, skipSources, cancellationToken))
			{
				yield return file;
			}
		}
	}

	private static bool IsSource(string relativePath)
	{
		var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		return _sourceExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase) ||
			segments.Any(segment => segment.StartsWith('.') ||
				segment.Equals("Properties", StringComparison.OrdinalIgnoreCase));
	}

	private static HashSet<string> ExcludedSet(IReadOnlyCollection<string> excludedDirectories)
		=> new(excludedDirectories.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))),
			PathComparer);

	private static StringComparison PathComparison => OperatingSystem.IsLinux()
		? StringComparison.Ordinal
		: StringComparison.OrdinalIgnoreCase;

	private static StringComparer PathComparer => OperatingSystem.IsLinux()
		? StringComparer.Ordinal
		: StringComparer.OrdinalIgnoreCase;
}
