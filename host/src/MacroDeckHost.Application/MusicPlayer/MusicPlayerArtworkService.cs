using System.Collections.Concurrent;
using System.Globalization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.MusicPlayer;

public sealed class MusicPlayerArtworkService : IMusicPlayerArtworkService
{
	private const long DefaultMaxCacheBytes = 16 * 1024 * 1024;
	private const string FallbackContentType = "application/octet-stream";

	private static readonly IReadOnlyDictionary<int, byte[]> _noVariants = new Dictionary<int, byte[]>();

	private readonly IMusicPlayerRegistry _registry;
	private readonly IArtworkProcessor _processor;
	private readonly ILogger _logger;
	private readonly long _maxCacheBytes;

	private readonly Lock _cacheLock = new();
	private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entriesByKey = new();
	private readonly LinkedList<CacheEntry> _lruOrder = new();
	private long _cachedBytes;

	private readonly ConcurrentDictionary<string, Lazy<Task<CacheEntry?>>> _inFlight = new();

	public MusicPlayerArtworkService(IMusicPlayerRegistry registry, IArtworkProcessor processor, ILogger logger)
		: this(registry, processor, logger, DefaultMaxCacheBytes)
	{
	}

	internal MusicPlayerArtworkService(IMusicPlayerRegistry registry,
		IArtworkProcessor processor,
		ILogger logger,
		long maxCacheBytes)
	{
		_registry = registry;
		_processor = processor;
		_logger = logger;
		_maxCacheBytes = maxCacheBytes;
	}

	public string GetETag(string artworkId, int? size)
	{
		var variant = size?.ToString(CultureInfo.InvariantCulture) ?? "master";
		return $"\"{artworkId}-{variant}\"";
	}

	public async Task<ArtworkImageResult?> GetImage(string instanceId,
		string artworkId,
		int? size,
		CancellationToken cancellationToken)
	{
		var key = $"{instanceId}\n{artworkId}";
		var entry = TryGetCached(key) ?? await FetchDeduplicated(key, instanceId, artworkId, cancellationToken);
		if (entry is null)
		{
			return null;
		}

		var variant = ArtworkVariants.Resolve(size, entry.Variants.Keys);
		var content = variant is null ? entry.Master : entry.Variants[variant.Value];
		return new ArtworkImageResult(content, entry.ContentType, GetETag(artworkId, size));
	}

	private Task<CacheEntry?> FetchDeduplicated(string key,
		string instanceId,
		string artworkId,
		CancellationToken cancellationToken)
	{
		var lazy = _inFlight.GetOrAdd(key,
			cacheKey => new Lazy<Task<CacheEntry?>>(() => FetchAndCache(cacheKey, instanceId, artworkId)));
		return lazy.Value.WaitAsync(cancellationToken);
	}

	private async Task<CacheEntry?> FetchAndCache(string key, string instanceId, string artworkId)
	{
		try
		{
			var player = _registry.GetPlayer(instanceId);
			if (player is null)
			{
				return null;
			}

			MacroDeck.Sdk.MusicPlayer.MusicPlayerArtwork? artwork;
			try
			{
				artwork = await player.GetArtworkAsync(artworkId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Failed to resolve artwork {ArtworkId} for {InstanceId}", artworkId, instanceId);
				return null;
			}

			if (artwork is null)
			{
				return null;
			}

			var processed = await _processor.Process(artwork.Data, CancellationToken.None);
			var entry = processed is null
				? new CacheEntry(key,
					artwork.Data,
					_noVariants,
					string.IsNullOrEmpty(artwork.MimeType) ? FallbackContentType : artwork.MimeType)
				: new CacheEntry(key, processed.MasterWebp, processed.Variants, "image/webp");

			Insert(entry);
			return entry;
		}
		finally
		{
			_inFlight.TryRemove(key, out _);
		}
	}

	private CacheEntry? TryGetCached(string key)
	{
		lock (_cacheLock)
		{
			if (!_entriesByKey.TryGetValue(key, out var node))
			{
				return null;
			}

			_lruOrder.Remove(node);
			_lruOrder.AddFirst(node);
			return node.Value;
		}
	}

	private void Insert(CacheEntry entry)
	{
		lock (_cacheLock)
		{
			if (_entriesByKey.ContainsKey(entry.Key))
			{
				return;
			}

			var node = _lruOrder.AddFirst(entry);
			_entriesByKey[entry.Key] = node;
			_cachedBytes += entry.ByteSize;

			while (_cachedBytes > _maxCacheBytes && _lruOrder.Count > 1)
			{
				var oldest = _lruOrder.Last!;
				_lruOrder.RemoveLast();
				_entriesByKey.Remove(oldest.Value.Key);
				_cachedBytes -= oldest.Value.ByteSize;
			}
		}
	}

	private sealed record CacheEntry(
		string Key,
		byte[] Master,
		IReadOnlyDictionary<int, byte[]> Variants,
		string ContentType)
	{
		public long ByteSize { get; } = Master.Length + Variants.Values.Sum(variant => (long)variant.Length);
	}
}
