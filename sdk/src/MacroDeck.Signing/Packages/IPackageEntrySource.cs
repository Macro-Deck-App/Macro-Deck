namespace MacroDeck.Signing.Packages;

/// <summary>One file in a package, named by its package-relative path with forward slashes.</summary>
/// <param name="Name">The package-relative path, e.g. <c>bin/plugin.dll</c>.</param>
/// <param name="Length">The file's length in bytes.</param>
/// <param name="IsRegularFile">
/// <c>false</c> for anything that is not a plain file - a symlink, device or fifo. Such an entry is
/// reported rather than skipped, so a package cannot smuggle one past the declared-files check.
/// </param>
internal sealed record PackageEntry(string Name, long Length, bool IsRegularFile);

/// <summary>
/// The files a package is made of, whether it is still an archive or has already been extracted. The two
/// live behind one interface so a signature is checked by the same code either way: an installed package
/// that verifies differently from the archive it came from would be a hole, not a convenience.
/// </summary>
internal interface IPackageEntrySource : IAsyncDisposable
{
	/// <summary>Every file in the package, in no particular order. Duplicates are preserved - an archive
	/// can carry the same name twice, and that is itself a failure the caller reports.</summary>
	IReadOnlyList<PackageEntry> Entries { get; }

	/// <summary>The first entry named <paramref name="name"/>, or <c>null</c>.</summary>
	PackageEntry? Find(string name);

	/// <summary>Opens <paramref name="entry"/> for reading.</summary>
	Task<Stream> OpenAsync(PackageEntry entry, CancellationToken cancellationToken);
}
