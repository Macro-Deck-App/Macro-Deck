using System.Collections.Concurrent;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces.KeyImages;

public sealed class DeviceKeyImageResources : IDeviceKeyImageResourceLookup
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IconAppearanceContext _context;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<(string ResourceId, int? Size), DeviceKeyImageResource?> _cache = new();
	private readonly ConcurrentDictionary<(string ResourceId, int? Size), byte> _inFlight = new();

	public DeviceKeyImageResources(IServiceScopeFactory scopeFactory, IconAppearanceContext context, ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_context = context;
		_logger = logger;
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
		return _cache.TryGetValue((resourceId, SizeBucket(displayPx)), out var resource) ? resource : null;
	}

	// A resource found absent stays absent until a tree changes, so a render never fetches in a loop.
	public void ForgetAbsent()
	{
		foreach (var (key, value) in _cache)
		{
			if (value is null)
			{
				_cache.TryRemove(key, out _);
			}
		}
	}

	public void Retain(IEnumerable<(string ResourceId, int? DisplayPx)> used)
	{
		var kept = used.Select(request => (request.ResourceId, SizeBucket(request.DisplayPx))).ToHashSet();
		foreach (var key in _cache.Keys)
		{
			if (!kept.Contains(key))
			{
				_cache.TryRemove(key, out _);
			}
		}
	}

	public async Task<bool> FetchAsync(
		IEnumerable<DeviceKeyImageResourceRequest> requests,
		CancellationToken cancellationToken)
	{
		var wanted = requests
			.Select(request => (request.ResourceId, Size: SizeBucket(request.DisplayPx)))
			.Distinct()
			.Where(key => !_cache.ContainsKey(key) && _inFlight.TryAdd(key, 0))
			.ToList();
		if (wanted.Count == 0)
		{
			return false;
		}

		var found = false;
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var store = scope.ServiceProvider.GetRequiredService<IUiResourceStore>();
			var pluginIcons = scope.ServiceProvider.GetService<IPluginIconUiResources>();
			var renditions = scope.ServiceProvider.GetService<IIconUiResourceRenditions>();
			foreach (var key in wanted)
			{
				var resource = await FindAsync(key.ResourceId,
					key.Size,
					store,
					pluginIcons,
					renditions,
					cancellationToken);
				_cache[key] = resource;
				found |= resource is not null;
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

		return found;
	}

	private async Task<DeviceKeyImageResource?> FindAsync(
		string resourceId,
		int? size,
		IUiResourceStore store,
		IPluginIconUiResources? pluginIcons,
		IIconUiResourceRenditions? renditions,
		CancellationToken cancellationToken)
	{
		var content = store.TryGet(resourceId, out var stored)
			? stored
			: pluginIcons is null
				? null
				: await pluginIcons.TryGetAsync(resourceId, cancellationToken);
		if (content is null)
		{
			return null;
		}

		if (size is { } requested
			&& renditions is not null
			&& await renditions.TryGetAsync(resourceId, requested, cancellationToken, _context) is { } sized)
		{
			content = sized.Content;
		}

		return new DeviceKeyImageResource(content.Content, content.MediaType, content.ContentHash);
	}
}
