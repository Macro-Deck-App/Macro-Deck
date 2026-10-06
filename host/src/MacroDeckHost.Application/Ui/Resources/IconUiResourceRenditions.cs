using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Ui.Resources;

public sealed record SizedIconResource(UiResourceContent Content, bool Stable);

public interface IIconUiResourceRenditions
{
	Task<SizedIconResource?> TryGetAsync(string resourceId,
		int requestedSize,
		CancellationToken cancellationToken,
		IconAppearanceContext? context = null);
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
		CancellationToken cancellationToken,
		IconAppearanceContext? context = null)
	{
		var size = WidgetIconRenditions.Bucket(requestedSize);
		if (Resolve(resourceId) is not var (source, reference, maxBytes, appearanceAware))
		{
			return null;
		}

		var selected = appearanceAware ? SelectAppearance(reference, context ?? IconAppearanceContext.None) : null;
		if (size == WidgetIconRenditions.DefaultSize && selected is null)
		{
			return null;
		}

		var version = source.GetVersion(reference);
		var key = $"{resourceId}|{size}|{version}|{selected}";
		if (version is not null && TryGetCached(key) is { } cached)
		{
			return new SizedIconResource(cached, true);
		}

		var rendition = await WidgetIconRenditions
			.ProduceAsync(source, selected?.ToString() ?? reference, size, maxBytes, cancellationToken)
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

	private Guid? SelectAppearance(string reference, IconAppearanceContext context)
	{
		if (!Guid.TryParse(reference, out var iconId) || _iconPackCache.GetIconById(iconId) is not { } icon)
		{
			return null;
		}

		var selected = IconAppearanceSelector.Select(icon, _iconPackCache.GetAppearances(icon.Id), context);
		return selected.Id == icon.Id ? null : selected.Id;
	}

	private (IWidgetIconSource Source, string Reference, int MaxBytes, bool AppearanceAware)? Resolve(string resourceId)
	{
		if (PluginIconReferences.TryParseResourceId(resourceId, out var iconId, out var pluginAppearanceAware))
		{
			return _iconPackCache.GetIconById(iconId) is { } icon &&
				_iconPackCache.GetPackById(icon.PackId) is not null &&
				FindSource(WidgetIconReference.IconPackType) is { } iconPackSource
					? (iconPackSource, iconId.ToString(), ProtocolLimits.MaxUiResourceBytes, pluginAppearanceAware)
					: null;
		}

		var prefix = WidgetIconResources.OwnerId + ".";
		if (!resourceId.StartsWith(prefix, StringComparison.Ordinal) ||
			resourceId.EndsWith(WidgetIconResources.ProtocolNameSuffix, StringComparison.Ordinal))
		{
			return null;
		}

		foreach (var source in _sources)
		{
			var typePrefix = prefix + source.Type + ".";
			if (resourceId.Length <= typePrefix.Length || !resourceId.StartsWith(typePrefix, StringComparison.Ordinal))
			{
				continue;
			}

			var reference = resourceId[typePrefix.Length..];
			var appearanceAware = source.Type == WidgetIconReference.IconPackType &&
				reference.EndsWith(WidgetIconResources.AppearanceNameSuffix, StringComparison.Ordinal);
			return (source,
				appearanceAware ? reference[..^WidgetIconResources.AppearanceNameSuffix.Length] : reference,
				HostUiResourceLimits.MaxHostIconResourceBytes,
				appearanceAware);
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
