using System.IO.Compression;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// Unpacks the parts of a Macro Deck 2 backup archive the migration actually reads, into a temporary
/// directory that behaves exactly like a live data directory.
/// </summary>
/// <remarks>
/// A backup holds the data directory at its root, and the bulk of it is <c>plugins/</c> - every plugin's
/// DLLs and, in one real backup, an 80 MB executable. That is 95 of its 102 MB and none of it is readable
/// by a migration, so only the entries below are unpacked. The archive is also written on Windows and
/// mixes <c>\</c> and <c>/</c> as separators, so entry names are normalised before anything looks at them.
/// </remarks>
internal sealed class MacroDeck2Archive : IDisposable
{
	/// <summary>
	/// Bounds against a zip bomb, not a limit on how big a backup may be: a real one unpacks to tens of
	/// megabytes once its plugin binaries are left behind, so nothing a person actually has comes near
	/// these.
	/// </summary>
	private const int MaxEntries = 500_000;

	private const long MaxTotalBytes = 16L * 1024 * 1024 * 1024;

	private static readonly string[] _directories =
		["profiles/", "iconpacks/", "configs/", "credentials/"];

	private static readonly string[] _files =
		["config.json", "devices.json", "profiles.db", "variables.db"];

	private MacroDeck2Archive(string root)
	{
		Root = root;
	}

	public string Root { get; }

	public static bool IsArchive(string path)
		=> Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Unpacks <paramref name="archivePath" />, or returns null when it is not readable as a Macro Deck 2
	/// backup. The caller disposes the result to delete the temporary directory again.
	/// </summary>
	public static MacroDeck2Archive? TryOpen(string archivePath)
	{
		var root = Path.Combine(Path.GetTempPath(),
			"macro-deck-migration",
			Guid.NewGuid().ToString("N"));

		var archive = new MacroDeck2Archive(root);
		try
		{
			Directory.CreateDirectory(root);
			using var zip = ZipFile.OpenRead(archivePath);

			var prefix = ResolveRootPrefix(zip);
			var entries = 0;
			var bytes = 0L;
			foreach (var entry in zip.Entries)
			{
				var relative = Wanted(entry.FullName, prefix);
				if (relative is null)
				{
					continue;
				}

				if (++entries > MaxEntries || (bytes += entry.Length) > MaxTotalBytes)
				{
					archive.Dispose();
					return null;
				}

				var destination = Path.Combine(root, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				entry.ExtractToFile(destination, overwrite: true);
			}

			return new MacroDeck2Paths(root).LooksLikeMacroDeck2() ? archive : Fail(archive);
		}
		catch (Exception ex) when (ex is InvalidDataException
			or IOException
			or UnauthorizedAccessException
			or NotSupportedException)
		{
			return Fail(archive);
		}
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	/// <summary>
	/// The path this entry unpacks to, or null when the migration does not read it. Also the place a
	/// hostile entry name is refused: anything rooted, drive-qualified or climbing out of the directory
	/// is dropped rather than sanitised, since none of the names this reads are allowed to contain them.
	/// </summary>
	private static string? Wanted(string entryName, string prefix)
	{
		var normalized = entryName.Replace('\\', '/');
		if (normalized.Length == 0 || normalized.EndsWith('/'))
		{
			return null;
		}

		if (prefix.Length > 0)
		{
			if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			normalized = normalized[prefix.Length..];
		}

		if (!IsWanted(normalized))
		{
			return null;
		}

		if (normalized.StartsWith('/') ||
			normalized.Contains(':', StringComparison.Ordinal) ||
			normalized.Split('/').Any(segment => segment is "." or ".."))
		{
			return null;
		}

		return normalized.Replace('/', Path.DirectorySeparatorChar);
	}

	/// <summary>
	/// Where the data directory begins inside the archive. Macro Deck 2's own backups write their entries
	/// at the root, while a folder compressed by hand nests them under its own name - the shape a
	/// right-click produces, at any depth.
	/// </summary>
	/// <remarks>
	/// Resolved by looking for the shallowest prefix under which a recognised entry appears, rather than
	/// by stripping whatever single directory happens to be common to every entry. Those are not the same
	/// thing: an archive holding only <c>profiles/</c> has one common directory too, and stripping it
	/// would throw the data away.
	/// </remarks>
	private static string ResolveRootPrefix(ZipArchive zip)
	{
		var names = zip.Entries
			.Select(entry => entry.FullName.Replace('\\', '/'))
			.Where(name => name.Length > 0 && !name.EndsWith('/'))
			.ToList();

		var prefix = string.Empty;
		while (true)
		{
			if (names.Any(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
				IsWanted(name[prefix.Length..])))
			{
				return prefix;
			}

			var separator = names
				.Select(name => name[prefix.Length..].IndexOf('/', StringComparison.Ordinal))
				.DefaultIfEmpty(-1)
				.First();

			if (separator <= 0)
			{
				return prefix;
			}

			var segment = names[0].Substring(prefix.Length, separator + 1);
			if (!names.All(name => name.AsSpan(prefix.Length).StartsWith(segment, StringComparison.OrdinalIgnoreCase)))
			{
				return prefix;
			}

			prefix += segment;
		}
	}

	private static bool IsWanted(string relative)
		=> _files.Contains(relative, StringComparer.OrdinalIgnoreCase) ||
			_directories.Any(directory => relative.StartsWith(directory, StringComparison.OrdinalIgnoreCase));

	private static MacroDeck2Archive? Fail(MacroDeck2Archive archive)
	{
		archive.Dispose();
		return null;
	}
}
