using System.IO.Compression;

namespace MacroDeckHost.Infrastructure.Store;

internal sealed class StoreRegistryArchive : IDisposable
{
	private readonly ZipArchive _zip;
	private readonly Dictionary<string, ZipArchiveEntry> _entries;

	private StoreRegistryArchive(ZipArchive zip, Dictionary<string, ZipArchiveEntry> entries)
	{
		_zip = zip;
		_entries = entries;
	}

	// GitHub archives put the whole tree under one folder named after the repository and ref. Entries are
	// keyed by their path below it; nothing is written to disk from here, only declared paths are read.
	public static StoreRegistryArchive? Open(byte[] bytes, out string? failure)
	{
		ZipArchive zip;
		try
		{
			zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
		}
		catch (InvalidDataException ex)
		{
			failure = $"The registry archive is not a readable zip file: {ex.Message}";
			return null;
		}

		var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
		string? root = null;
		foreach (var entry in zip.Entries)
		{
			var separator = entry.FullName.IndexOf('/');
			var entryRoot = separator < 0 ? entry.FullName : entry.FullName[..separator];
			if (root is null)
			{
				root = entryRoot;
			}
			else if (!string.Equals(root, entryRoot, StringComparison.Ordinal))
			{
				zip.Dispose();
				failure = $"The registry archive entry '{entry.FullName}' is outside the archive's root folder.";
				return null;
			}

			var relative = separator < 0 ? string.Empty : entry.FullName[(separator + 1)..];
			if (relative.Length == 0 || relative.EndsWith('/'))
			{
				continue;
			}

			if (!entries.TryAdd(relative, entry))
			{
				zip.Dispose();
				failure = $"The registry archive contains '{relative}' more than once.";
				return null;
			}
		}

		failure = null;
		return new StoreRegistryArchive(zip, entries);
	}

	public bool Contains(string path)
	{
		return _entries.ContainsKey(path);
	}

	public StoreFetch Read(string path, long maxBytes, long? expectedSize)
	{
		if (!_entries.TryGetValue(path, out var entry))
		{
			return StoreFetch.Fail($"The registry archive does not contain '{path}'.");
		}

		var limit = expectedSize is > 0 ? Math.Min(expectedSize.Value, maxBytes) : maxBytes;
		if (entry.Length > limit)
		{
			return StoreFetch.Fail($"'{path}' in the registry archive is {entry.Length} bytes, above {limit}.",
				expectedSize is > 0);
		}

		try
		{
			using var stream = entry.Open();
			using var buffer = new MemoryStream();
			var chunk = new byte[81_920];
			var total = 0L;
			int read;
			while ((read = stream.Read(chunk)) > 0)
			{
				total += read;
				if (total > limit)
				{
					return StoreFetch.Fail($"'{path}' in the registry archive exceeded {limit} bytes.",
						expectedSize is > 0);
				}

				buffer.Write(chunk, 0, read);
			}

			if (expectedSize is > 0 && total != expectedSize)
			{
				return StoreFetch.Fail($"'{path}' in the registry archive is {total} bytes, not the declared "
					+ $"{expectedSize}.",
					true);
			}

			return StoreFetch.Ok(buffer.ToArray());
		}
		catch (InvalidDataException ex)
		{
			return StoreFetch.Fail($"'{path}' in the registry archive could not be read: {ex.Message}");
		}
	}

	public void Dispose()
	{
		_zip.Dispose();
	}
}
