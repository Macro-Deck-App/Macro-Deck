using MacroDeck.Plugin.Protocol.Assets;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Ui.Resources;

public sealed record SizedIconResource(UiResourceContent Content, bool Stable);

public interface IIconUiResourceRenditions
{
	Task<SizedIconResource?> TryGetAsync(string resourceId, int requestedSize, CancellationToken cancellationToken);
}

public sealed class IconUiResourceRenditions : IIconUiResourceRenditions
{
	internal const long MaxCachedBytes = 16 * 1024 * 1024;

	private readonly IReadOnlyList<IWidgetIconSource> _sources;
	private readonly IIconPackCache _iconPackCache;
	private readonly Dictionary<string, LinkedListNode<(string Key, UiResourceContent Content)>> _entries =
		new(StringComparer.Ordinal);
	private readonly LinkedList<(string Key, UiResourceContent Content)> _recency = new();
	private readonly Lock _sync = new();
	private long _cachedBytes;

	public IconUiResourceRenditions(IEnumerable<IWidgetIconSource> sources, IIconPackCache iconPackCache)
	{
		_sources = sources.ToList();
		_iconPackCache = iconPackCache;
	}

	public async Task<SizedIconResource?> TryGetAsync(string resourceId,
		int requestedSize,
		CancellationToken cancellationToken)
	{
		var size = WidgetIconRenditions.Bucket(requestedSize);
		if (size == WidgetIconRenditions.DefaultSize || Resolve(resourceId) is not var (source, reference))
		{
			return null;
		}

		var version = source.GetVersion(reference);
		var key = $"{resourceId}|{size}|{version}";
		if (version is not null && TryGetCached(key) is { } cached)
		{
			return new SizedIconResource(cached, true);
		}

		var rendition = await WidgetIconRenditions.ProduceAsync(source, reference, size, cancellationToken)
			.ConfigureAwait(false);
		if (rendition.Status != WidgetIconRenditionStatus.Rendered)
		{
			return null;
		}

		var content = new UiResourceContent
		{
			Content = rendition.Content!,
			MediaType = rendition.MediaType!,
			ContentHash = AssetContentHash.Compute(rendition.Content)
		};

		if (rendition.Stable && version is not null)
		{
			Store(key, content);
		}

		return new SizedIconResource(content, rendition.Stable);
	}

	private (IWidgetIconSource Source, string Reference)? Resolve(string resourceId)
	{
		if (PluginIconReferences.TryParseResourceId(resourceId, out var iconId))
		{
			return _iconPackCache.GetIconById(iconId) is { } icon &&
				_iconPackCache.GetPackById(icon.PackId) is not null &&
				FindSource(WidgetIconReference.IconPackType) is { } iconPackSource
					? (iconPackSource, iconId.ToString())
					: null;
		}

		var prefix = WidgetIconResources.OwnerId + ".";
		if (!resourceId.StartsWith(prefix, StringComparison.Ordinal))
		{
			return null;
		}

		foreach (var source in _sources)
		{
			var typePrefix = prefix + source.Type + ".";
			if (resourceId.Length > typePrefix.Length && resourceId.StartsWith(typePrefix, StringComparison.Ordinal))
			{
				return (source, resourceId[typePrefix.Length..]);
			}
		}

		return null;
	}

	private IWidgetIconSource? FindSource(string type)
		=> _sources.FirstOrDefault(source => string.Equals(source.Type, type, StringComparison.Ordinal));

	private UiResourceContent? TryGetCached(string key)
	{
		lock (_sync)
		{
			if (!_entries.TryGetValue(key, out var node))
			{
				return null;
			}

			_recency.Remove(node);
			_recency.AddLast(node);
			return node.Value.Content;
		}
	}

	private void Store(string key, UiResourceContent content)
	{
		lock (_sync)
		{
			if (_entries.Remove(key, out var existing))
			{
				_cachedBytes -= existing.Value.Content.Content.Length;
				_recency.Remove(existing);
			}

			_entries[key] = _recency.AddLast((key, content));
			_cachedBytes += content.Content.Length;

			while (_cachedBytes > MaxCachedBytes && _recency.First is { } oldest)
			{
				_cachedBytes -= oldest.Value.Content.Content.Length;
				_entries.Remove(oldest.Value.Key);
				_recency.RemoveFirst();
			}
		}
	}
}
