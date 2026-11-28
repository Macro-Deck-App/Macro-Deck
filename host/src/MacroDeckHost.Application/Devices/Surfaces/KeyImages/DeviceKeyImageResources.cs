using System.Collections.Concurrent;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces.KeyImages;

public sealed class DeviceKeyImageResources : IDeviceKeyImageResourceLookup
{
	internal const int MaxUnusedEntries = 64;

	internal static readonly TimeSpan RetainFor = TimeSpan.FromSeconds(30);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IconAppearanceContext _context;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly IUiResourceStore? _store;
	private readonly ConcurrentDictionary<(string ResourceId, int? Size), Entry> _cache = new();
	private readonly ConcurrentDictionary<(string ResourceId, int? Size), byte> _inFlight = new();

	public DeviceKeyImageResources(
		IServiceScopeFactory scopeFactory,
		IconAppearanceContext context,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_context = context;
		_timeProvider = timeProvider;
		_logger = logger;
		using var scope = scopeFactory.CreateScope();
		_store = scope.ServiceProvider.GetService<IUiResourceStore>();
	}

	// The buckets of ui/runtime's icon-prefetch.util.ts, so a key asks for the renditions a client asks for.
	public static int? SizeBucket(int? displayPx)
	{
		return displayPx switch
		{
			null => null,
			<= 128 => 128,
			<= 256 => 256,
			_ => 512
		};
	}

	public DeviceKeyImageResource? TryGet(string resourceId, int? displayPx)
	{
		return Lookup(resourceId, displayPx, out _);
	}

	public DeviceKeyImageResource? Lookup(string resourceId, int? displayPx, out bool pending)
	{
		var key = (resourceId, SizeBucket(displayPx));
		if (!_cache.TryGetValue(key, out var entry))
		{
			pending = true;
			return null;
		}

		var current = _store is not null && _store.TryGet(resourceId, out var stored) ? stored.ContentHash : null;
		if (entry.Resource is null
			? current is not null
			: entry.SourceHash is not null && !string.Equals(current, entry.SourceHash, StringComparison.Ordinal))
		{
			_cache.TryRemove(new KeyValuePair<(string, int?), Entry>(key, entry));
			pending = true;
			return null;
		}

		pending = false;
		return entry.Resource;
	}

	public void ForgetAbsent()
	{
		foreach (var (key, entry) in _cache)
		{
			if (entry is { Resource: null, Stale: false })
			{
				_cache.TryUpdate(key, entry with { Stale = true }, entry);
			}
		}
	}

	public void Retain(IEnumerable<(string ResourceId, int? DisplayPx)> used)
	{
		var now = _timeProvider.GetUtcNow();
		var kept = used.Select(request => (request.ResourceId, SizeBucket(request.DisplayPx))).ToHashSet();
		foreach (var key in kept)
		{
			if (_cache.TryGetValue(key, out var entry))
			{
				_cache.TryUpdate(key, entry with { LastUsed = now }, entry);
			}
		}

		var unused = new List<((string ResourceId, int? Size) Key, DateTimeOffset LastUsed)>();
		foreach (var (key, entry) in _cache)
		{
			if (kept.Contains(key))
			{
				continue;
			}

			if (now - entry.LastUsed > RetainFor)
			{
				_cache.TryRemove(key, out _);
			}
			else
			{
				unused.Add((key, entry.LastUsed));
			}
		}

		foreach (var (key, _) in unused.OrderBy(entry => entry.LastUsed).Take(unused.Count - MaxUnusedEntries))
		{
			_cache.TryRemove(key, out _);
		}
	}

	public async Task<DeviceKeyImageFetch> FetchAsync(
		IEnumerable<DeviceKeyImageResourceRequest> requests,
		CancellationToken cancellationToken)
	{
		var wanted = requests
			.Select(request => (request.ResourceId, Size: SizeBucket(request.DisplayPx)))
			.Distinct()
			.Where(key => (!_cache.TryGetValue(key, out var entry) || entry.Stale) && _inFlight.TryAdd(key, 0))
			.ToList();
		if (wanted.Count == 0)
		{
			return new DeviceKeyImageFetch(false, false);
		}

		var found = false;
		var unblocked = false;
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var store = scope.ServiceProvider.GetRequiredService<IUiResourceStore>();
			var pluginIcons = scope.ServiceProvider.GetService<IPluginIconUiResources>();
			var renditions = scope.ServiceProvider.GetService<IIconUiResourceRenditions>();
			foreach (var key in wanted)
			{
				Entry entry;
				try
				{
					entry = await FindAsync(key.ResourceId,
						key.Size,
						store,
						pluginIcons,
						renditions,
						cancellationToken);
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					_logger.Warning(exception, "Failed to fetch a resource a device key shows");
					entry = new Entry(null, null, _timeProvider.GetUtcNow(), false);
				}

				unblocked |= !_cache.ContainsKey(key);
				_cache[key] = entry;
				found |= entry.Resource is not null;
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Warning(exception, "Failed to fetch a resource a device key shows");
		}
		finally
		{
			foreach (var key in wanted)
			{
				_inFlight.TryRemove(key, out _);
			}
		}

		return new DeviceKeyImageFetch(found, unblocked);
	}

	private async Task<Entry> FindAsync(
		string resourceId,
		int? size,
		IUiResourceStore store,
		IPluginIconUiResources? pluginIcons,
		IIconUiResourceRenditions? renditions,
		CancellationToken cancellationToken)
	{
		var now = _timeProvider.GetUtcNow();
		var fromStore = store.TryGet(resourceId, out var stored);
		var content = fromStore
			? stored
			: pluginIcons is null
				? null
				: await pluginIcons.TryGetAsync(resourceId, cancellationToken);
		if (content is null)
		{
			return new Entry(null, null, now, false);
		}

		var sourceHash = fromStore ? stored.ContentHash : null;
		if (size is { } requested
			&& renditions is not null
			&& await renditions.TryGetAsync(resourceId, requested, cancellationToken, _context) is { } sized)
		{
			content = sized.Content;
		}

		return new Entry(new DeviceKeyImageResource(content.Content, content.MediaType, content.ContentHash),
			sourceHash,
			now,
			false);
	}

	private sealed record Entry(
		DeviceKeyImageResource? Resource,
		string? SourceHash,
		DateTimeOffset LastUsed,
		bool Stale);
}

public readonly record struct DeviceKeyImageFetch(bool Found, bool Unblocked);
