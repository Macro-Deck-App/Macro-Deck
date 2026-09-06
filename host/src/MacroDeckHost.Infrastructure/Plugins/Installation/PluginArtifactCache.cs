using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Infrastructure.Persistence;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

public sealed class PluginArtifactCache : IPluginArtifactCache
{
	private readonly string _cacheDirectory;
	private readonly string _indexPath;
	private readonly PluginInstallerOptions _options;
	private readonly ILogger _logger;
	private readonly object _lock = new();
	private readonly DurableJsonFile _files;

	public PluginArtifactCache(IMacroDeckPaths paths,
		PluginInstallerOptions options,
		ILogger logger)
	{
		_cacheDirectory = paths.PluginCacheDirectory;
		_indexPath = Path.Combine(_cacheDirectory, "index.json");
		_options = options;
		_logger = logger.ForContext<PluginArtifactCache>();
		_files = new DurableJsonFile("plugin cache index", PersistenceJsonOptions.Default, _logger);
	}

	public bool TryGet(string sha256, [NotNullWhen(true)] out string? cachedPath)
	{
		var key = NormalizeKey(sha256);
		cachedPath = null;

		lock (_lock)
		{
			var index = LoadIndexLocked();
			if (!index.TryGetValue(key, out var entry))
			{
				return false;
			}

			var path = CachedFilePath(key);
			if (!File.Exists(path))
			{
				index.Remove(key);
				SaveIndexLocked(index);
				PluginInstallInfrastructureLog.CacheEntryFileMissing(_logger, key, path);
				return false;
			}

			index[key] = entry with { LastAccessUtc = DateTimeOffset.UtcNow };
			SaveIndexLocked(index);
			cachedPath = path;
			return true;
		}
	}

	public async Task<string> Put(string sourcePath, string sha256, CancellationToken cancellationToken = default)
	{
		var key = NormalizeKey(sha256);
		Directory.CreateDirectory(_cacheDirectory);
		var destinationPath = CachedFilePath(key);

		try
		{
			File.Move(sourcePath, destinationPath, overwrite: true);
		}
		catch (IOException)
		{
			await using (var source = File.OpenRead(sourcePath))
			await using (var destination = File.Create(destinationPath))
			{
				await source.CopyToAsync(destination, cancellationToken);
			}

			File.Delete(sourcePath);
		}

		var sizeBytes = new FileInfo(destinationPath).Length;
		var now = DateTimeOffset.UtcNow;

		lock (_lock)
		{
			var index = LoadIndexLocked();
			index[key] = new CacheIndexEntry
			{
				Sha256 = key,
				SizeBytes = sizeBytes,
				CachedAtUtc = now,
				LastAccessUtc = now
			};

			EvictToBudgetLocked(index);
			SaveIndexLocked(index);
		}

		return destinationPath;
	}

	public void Prune()
	{
		lock (_lock)
		{
			var index = LoadIndexLocked();

			foreach (var key in index.Keys.ToList())
			{
				var path = CachedFilePath(key);
				if (!File.Exists(path))
				{
					index.Remove(key);
					PluginInstallInfrastructureLog.CacheEntryFileMissing(_logger, key, path);
				}
			}

			if (Directory.Exists(_cacheDirectory))
			{
				foreach (var filePath in Directory.EnumerateFiles(_cacheDirectory,
					"*" + PluginArtifactFiles.MacroDeckPluginExtension))
				{
					var key = Path.GetFileNameWithoutExtension(filePath);
					if (!index.ContainsKey(key))
					{
						PluginInstallInfrastructureLog.CacheOrphanFileDeleted(_logger, filePath);
						TryDeleteFile(filePath);
					}
				}
			}

			EvictToBudgetLocked(index);
			SaveIndexLocked(index);
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			if (Directory.Exists(_cacheDirectory))
			{
				foreach (var filePath in Directory.EnumerateFiles(_cacheDirectory))
				{
					TryDeleteFile(filePath);
				}
			}

			SaveIndexLocked(new Dictionary<string, CacheIndexEntry>(StringComparer.OrdinalIgnoreCase));
		}
	}

	private void EvictToBudgetLocked(Dictionary<string, CacheIndexEntry> index)
	{
		var total = index.Values.Sum(entry => entry.SizeBytes);
		if (total <= _options.CacheBudgetBytes)
		{
			return;
		}

		foreach (var entry in index.Values.OrderBy(entry => entry.LastAccessUtc).ToList())
		{
			if (total <= _options.CacheBudgetBytes)
			{
				break;
			}

			TryDeleteFile(CachedFilePath(entry.Sha256));
			index.Remove(entry.Sha256);
			total -= entry.SizeBytes;
			PluginInstallInfrastructureLog.CacheEntryEvicted(_logger, entry.Sha256, _options.CacheBudgetBytes);
		}
	}

	private Dictionary<string, CacheIndexEntry> LoadIndexLocked()
	{
		var entries = _files.Read<List<CacheIndexEntry>>(_indexPath) ?? [];
		return entries.ToDictionary(entry => entry.Sha256, StringComparer.OrdinalIgnoreCase);
	}

	private void SaveIndexLocked(Dictionary<string, CacheIndexEntry> index)
	{
		try
		{
			Directory.CreateDirectory(_cacheDirectory);
			_files.Write(_indexPath, index.Values.ToList());
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			PluginInstallInfrastructureLog.CacheIndexWriteFailed(_logger, _indexPath, ex);
		}
	}

	private void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			PluginInstallInfrastructureLog.CacheFileDeleteFailed(_logger, path, ex);
		}
	}

	private string CachedFilePath(string key) =>
		Path.Combine(_cacheDirectory, key + PluginArtifactFiles.MacroDeckPluginExtension);

	private static string NormalizeKey(string sha256)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

		var withoutPrefix = sha256.StartsWith(AssetContentHash.Sha256Prefix, StringComparison.OrdinalIgnoreCase)
			? sha256[AssetContentHash.Sha256Prefix.Length..]
			: sha256;

		var key = withoutPrefix.ToLowerInvariant();
		if (key.Length != 64 || !key.All(char.IsAsciiHexDigitLower))
		{
			throw new ArgumentException($"'{sha256}' is not a SHA-256 digest.", nameof(sha256));
		}

		return key;
	}

	private sealed record CacheIndexEntry
	{
		public required string Sha256 { get; init; }

		public required long SizeBytes { get; init; }

		public required DateTimeOffset CachedAtUtc { get; init; }

		public DateTimeOffset LastAccessUtc { get; init; }
	}
}
