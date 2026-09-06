using System.IO.Compression;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Signing.Packages;

/// <summary>A package still in its <c>.zip</c> container.</summary>
internal sealed class ZipPackageEntrySource : IPackageEntrySource
{
	private readonly ZipArchive _archive;
	private readonly bool _ownsArchive;
	private readonly Dictionary<string, ZipArchiveEntry> _entriesByName = new(StringComparer.Ordinal);

	private ZipPackageEntrySource(ZipArchive archive, bool ownsArchive)
	{
		_archive = archive;
		_ownsArchive = ownsArchive;

		var entries = new List<PackageEntry>();
		foreach (var entry in archive.Entries)
		{
			if (entry.FullName.EndsWith('/'))
			{
				continue;
			}

			entries.Add(new PackageEntry(entry.FullName,
				entry.Length,
				!PluginArtifactEntryPolicy.IsUnsafeFileType(entry.ExternalAttributes)));

			_entriesByName.TryAdd(entry.FullName, entry);
		}

		Entries = entries;
	}

	public IReadOnlyList<PackageEntry> Entries { get; }

	public static async Task<ZipPackageEntrySource> OpenAsync(string packagePath, CancellationToken cancellationToken)
	{
		return new ZipPackageEntrySource(await ZipFile.OpenReadAsync(packagePath, cancellationToken),
			ownsArchive: true);
	}

	/// <summary>Views an archive the caller keeps open and disposes itself.</summary>
	public static ZipPackageEntrySource Wrap(ZipArchive archive)
	{
		return new ZipPackageEntrySource(archive, ownsArchive: false);
	}

	public PackageEntry? Find(string name)
	{
		return Entries.FirstOrDefault(entry => entry.Name == name);
	}

	public async Task<Stream> OpenAsync(PackageEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		return await _entriesByName[entry.Name].OpenAsync(cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		return _ownsArchive ? _archive.DisposeAsync() : ValueTask.CompletedTask;
	}
}
