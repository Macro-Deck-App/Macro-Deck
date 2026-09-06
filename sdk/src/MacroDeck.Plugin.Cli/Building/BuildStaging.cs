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

	public static string Create(string? stagingRoot)
	{
		var root = stagingRoot ?? Path.GetTempPath();
		var directory = Path.Combine(root, $"macrodeck-plugin-build-{Guid.NewGuid():N}");

		Directory.CreateDirectory(directory);

		return directory;
	}

	/// <summary>Copies everything beside the manifest into the staging root, so a manifest-relative
	/// reference such as <c>assets/icon.png</c> resolves to the same path inside the package. The manifest
	/// itself is written separately - <see cref="PluginBuilder" /> stages a rewritten copy.</summary>
	/// <param name="excludedDirectories">Absolute paths never copied: each target's configured build output
	/// and each runtime identifier's staging destination, which are staged from their real sources instead.</param>
	public static void CopySharedAssets(string sourceRoot,
		string stagingRoot,
		IReadOnlyCollection<string> excludedDirectories,
		CancellationToken cancellationToken)
	{
		var excluded = new HashSet<string>(excludedDirectories.Select(Path.GetFullPath), PathComparer);

		CopyDirectory(Path.GetFullPath(sourceRoot),
			Path.GetFullPath(sourceRoot),
			stagingRoot,
			excluded,
			cancellationToken);
	}

	public static void CopyTargetOutput(string outputDirectory,
		string destinationDirectory,
		CancellationToken cancellationToken)
	{
		CopyDirectory(Path.GetFullPath(outputDirectory),
			Path.GetFullPath(outputDirectory),
			destinationDirectory,
			new HashSet<string>(StringComparer.Ordinal),
			cancellationToken);
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

	private static void CopyDirectory(string root,
		string current,
		string destination,
		IReadOnlySet<string> excluded,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(destination);

		foreach (var file in Directory.EnumerateFiles(current))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var name = Path.GetFileName(file);

			if (_excludedFiles.Contains(name, StringComparer.OrdinalIgnoreCase) ||
				name.Equals(PluginBuildConfigReader.FileName, StringComparison.OrdinalIgnoreCase) ||
				(current.Equals(root, StringComparison.Ordinal) &&
					name.Equals(PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			File.Copy(file, Path.Combine(destination, name), overwrite: true);
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

			CopyDirectory(root, directory, Path.Combine(destination, name), excluded, cancellationToken);
		}
	}

	private static StringComparer PathComparer => OperatingSystem.IsLinux()
		? StringComparer.Ordinal
		: StringComparer.OrdinalIgnoreCase;
}
