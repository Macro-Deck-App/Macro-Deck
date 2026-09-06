using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Assets;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Plugins;

public sealed class PluginAssetDiskCache : IPluginAssetCache
{
	internal const long DefaultMaxMemoryBytes = 16L * ProtocolLimits.MaxAssetBytes;

	internal const long DefaultMaxDiskBytes = 128L * ProtocolLimits.MaxAssetBytes;

	private readonly string _directory;
	private readonly ILogger _logger;
	private readonly long _maxMemoryBytes;
	private readonly long _maxDiskBytes;

	private readonly Lock _gate = new();
	private readonly Dictionary<string, (byte[] Bytes, string MimeType)> _memory = new(StringComparer.Ordinal);

	private readonly LinkedList<string> _lruOrder = new();
	private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.Ordinal);
	private long _memoryBytes;

	public PluginAssetDiskCache(IMacroDeckPaths paths,
		ILogger logger,
		long maxMemoryBytes = DefaultMaxMemoryBytes,
		long maxDiskBytes = DefaultMaxDiskBytes)
	{
		_directory = Path.Combine(paths.PluginsDirectory, "assets");
		_logger = logger;
		_maxMemoryBytes = maxMemoryBytes;
		_maxDiskBytes = maxDiskBytes;
	}

	public void Write(string contentHash, string mimeType, byte[] bytes)
	{
		ArgumentException.ThrowIfNullOrEmpty(contentHash);
		ArgumentNullException.ThrowIfNull(bytes);

		// Defense in depth: the only caller (PluginAssetReceiver.Commit) always passes a hash it just
		// computed itself, so this never legitimately fails - it exists so a future caller cannot
		// accidentally reintroduce the path-traversal hole PathsFor's callers already guard against.
		if (!AssetContentHash.IsValid(contentHash))
		{
			_logger.Error("Refusing to cache asset with a malformed content hash {ContentHash}", contentHash);
			return;
		}

		InsertIntoMemory(contentHash, bytes, mimeType);

		try
		{
			Directory.CreateDirectory(_directory);
			var (dataPath, metaPath) = PathsFor(contentHash);

			var tempData = dataPath + ".tmp";
			File.WriteAllBytes(tempData, bytes);
			File.Move(tempData, dataPath, overwrite: true);

			var tempMeta = metaPath + ".tmp";
			File.WriteAllText(tempMeta, mimeType);
			File.Move(tempMeta, metaPath, overwrite: true);

			PruneDiskIfNeeded();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Error(exception, "Failed to persist cached asset {ContentHash} to disk", contentHash);
		}
	}

	public bool TryRead(string contentHash, out byte[] bytes, out string mimeType)
	{
		lock (_gate)
		{
			if (_memory.TryGetValue(contentHash, out var cached))
			{
				Touch(contentHash);
				bytes = cached.Bytes;
				mimeType = cached.MimeType;
				return true;
			}
		}

		// Defense in depth: every caller of TryRead is expected to reject a malformed hash itself before
		// it gets here (a plugin-supplied hash off the wire must never reach PathsFor's Path.Combine
		// unchecked - see AssetContentHash.IsValid's remarks), but this is the last line standing between
		// an untrusted string and the disk if a future caller forgets.
		if (!AssetContentHash.IsValid(contentHash))
		{
			bytes = [];
			mimeType = string.Empty;
			return false;
		}

		var (dataPath, metaPath) = PathsFor(contentHash);
		var info = new FileInfo(dataPath);
		if (!info.Exists)
		{
			bytes = [];
			mimeType = string.Empty;
			return false;
		}

		// A hash-named file this large could never have passed the upload-time check that produced it
		// (ProtocolLimits.MaxAssetBytes); reject it rather than reading an arbitrarily large file into
		// memory and pinning it in the cache forever.
		if (info.Length > ProtocolLimits.MaxAssetBytes)
		{
			_logger.Error("Refusing to read cached asset {ContentHash}: {Length} bytes exceeds the {Max} byte limit",
				contentHash,
				info.Length,
				ProtocolLimits.MaxAssetBytes);
			bytes = [];
			mimeType = string.Empty;
			return false;
		}

		try
		{
			bytes = File.ReadAllBytes(dataPath);
			mimeType = File.Exists(metaPath) ? File.ReadAllText(metaPath) : "application/octet-stream";
			InsertIntoMemory(contentHash, bytes, mimeType);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Error(exception, "Failed to read cached asset {ContentHash} from disk", contentHash);
			bytes = [];
			mimeType = string.Empty;
			return false;
		}
	}

	private void InsertIntoMemory(string contentHash, byte[] bytes, string mimeType)
	{
		lock (_gate)
		{
			if (_memory.TryGetValue(contentHash, out var existing))
			{
				_memoryBytes -= existing.Bytes.Length;
			}

			_memory[contentHash] = (bytes, mimeType);
			_memoryBytes += bytes.Length;
			Touch(contentHash);
			EvictMemoryIfNeeded();
		}
	}

	private void Touch(string contentHash)
	{
		if (_lruNodes.TryGetValue(contentHash, out var node))
		{
			_lruOrder.Remove(node);
		}

		_lruNodes[contentHash] = _lruOrder.AddLast(contentHash);
	}

	private void EvictMemoryIfNeeded()
	{
		while (_memoryBytes > _maxMemoryBytes && _lruOrder.First is { } oldest)
		{
			_lruOrder.RemoveFirst();
			_lruNodes.Remove(oldest.Value);

			if (_memory.Remove(oldest.Value, out var evicted))
			{
				_memoryBytes -= evicted.Bytes.Length;
			}
		}
	}

	private void PruneDiskIfNeeded()
	{
		try
		{
			var files = new DirectoryInfo(_directory).GetFiles("*.bin");
			var totalBytes = files.Sum(file => file.Length);

			if (totalBytes <= _maxDiskBytes)
			{
				return;
			}

			foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
			{
				if (totalBytes <= _maxDiskBytes)
				{
					break;
				}

				var contentHash = "sha256:" + Path.GetFileNameWithoutExtension(file.Name);
				var (dataPath, metaPath) = PathsFor(contentHash);

				totalBytes -= file.Length;
				TryDelete(dataPath);
				TryDelete(metaPath);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Error(exception, "Failed to prune the plugin asset cache directory");
		}
	}

	private void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Error(exception, "Failed to delete pruned cache file {Path}", path);
		}
	}

	private (string DataPath, string MetaPath) PathsFor(string contentHash)
	{
		var colon = contentHash.IndexOf(':');
		var fileName = colon >= 0 ? contentHash[(colon + 1)..] : contentHash;

		return (Path.Combine(_directory, fileName + ".bin"), Path.Combine(_directory, fileName + ".mime"));
	}
}
