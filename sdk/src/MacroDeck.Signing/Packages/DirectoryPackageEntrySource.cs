namespace MacroDeck.Signing.Packages;

/// <summary>
/// A package that has already been extracted onto disk, so an installed copy can be re-verified without
/// keeping the archive it came from.
/// </summary>
/// <remarks>
/// Symlinks and reparse points are reported as entries that are not regular files rather than followed,
/// and symlinked directories are not descended into. Following either would let a link planted in a
/// user-writable plugin directory point verification at bytes other than the ones that will be loaded.
/// </remarks>
internal sealed class DirectoryPackageEntrySource : IPackageEntrySource
{
	private readonly string _rootDirectory;

	private DirectoryPackageEntrySource(string rootDirectory, IReadOnlyList<PackageEntry> entries)
	{
		_rootDirectory = rootDirectory;
		Entries = entries;
	}

	public IReadOnlyList<PackageEntry> Entries { get; }

	public static DirectoryPackageEntrySource Open(string rootDirectory)
	{
		var root = Path.GetFullPath(rootDirectory);
		var entries = new List<PackageEntry>();
		Collect(root, root, entries);

		return new DirectoryPackageEntrySource(root, entries);
	}

	public PackageEntry? Find(string name)
	{
		return Entries.FirstOrDefault(entry => entry.Name == name);
	}

	public Task<Stream> OpenAsync(PackageEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		var path = Path.Combine(_rootDirectory, entry.Name.Replace('/', Path.DirectorySeparatorChar));
		return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private static void Collect(string root, string directory, List<PackageEntry> entries)
	{
		foreach (var path in Directory.EnumerateFiles(directory))
		{
			var info = new FileInfo(path);
			var isRegularFile = !info.Attributes.HasFlag(FileAttributes.ReparsePoint);

			entries.Add(new PackageEntry(RelativeName(root, path),
				isRegularFile ? info.Length : 0,
				isRegularFile));
		}

		foreach (var path in Directory.EnumerateDirectories(directory))
		{
			if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
			{
				entries.Add(new PackageEntry(RelativeName(root, path), 0, IsRegularFile: false));
				continue;
			}

			Collect(root, path, entries);
		}
	}

	private static string RelativeName(string root, string path)
	{
		return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
	}
}
