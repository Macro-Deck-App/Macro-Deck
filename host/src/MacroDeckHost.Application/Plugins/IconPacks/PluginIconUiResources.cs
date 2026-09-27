using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public enum PluginIconResourceStatus
{
	Found,
	NotFound,
	TooLarge
}

public sealed record PluginIconResourceResult(PluginIconResourceStatus Status, UiResource? Handle = null);

public interface IPluginIconUiResources
{
	Task<PluginIconResourceResult> GetHandleAsync(string pluginId, string key, string name, CancellationToken cancellationToken);

	Task<UiResourceContent?> TryGetAsync(string resourceId, CancellationToken cancellationToken);
}

public sealed class PluginIconUiResources(
	IIconPackCache iconPackCache,
	IPluginIconResolver resolver,
	IWidgetIconSourceRegistry sources) : IPluginIconUiResources
{
	internal const int MaxEntries = 256;

	private readonly LinkedList<(Guid IconId, string Hash, UiResourceContent Content)> _recency = new();
	private readonly Lock _sync = new();

	public async Task<PluginIconResourceResult> GetHandleAsync(string pluginId,
		string key,
		string name,
		CancellationToken cancellationToken)
	{
		if (resolver.Resolve(pluginId, key, name) is not { } icon)
		{
			return new PluginIconResourceResult(PluginIconResourceStatus.NotFound);
		}

		var (status, content) = await ProduceAsync(icon, cancellationToken);
		return status != PluginIconResourceStatus.Found
			? new PluginIconResourceResult(status)
			: new PluginIconResourceResult(status,
				new UiResource
				{
					ResourceId = PluginIconReferences.ResourceId(icon.Id),
					ContentHash = content!.ContentHash,
					MediaType = content.MediaType,
					ByteLength = content.Content.Length
				});
	}

	public async Task<UiResourceContent?> TryGetAsync(string resourceId, CancellationToken cancellationToken)
	{
		if (!PluginIconReferences.TryParseResourceId(resourceId, out var iconId) ||
			iconPackCache.GetIconById(iconId) is not { } icon ||
			iconPackCache.GetPackById(icon.PackId) is not { SourceType: IconPackSourceType.Plugin })
		{
			return null;
		}

		var (_, content) = await ProduceAsync(icon, cancellationToken);
		return content;
	}

	private async Task<(PluginIconResourceStatus Status, UiResourceContent? Content)> ProduceAsync(IconEntity icon,
		CancellationToken cancellationToken)
	{
		var hash = icon.MasterContentHash ?? icon.SourceContentHash ?? string.Empty;
		if (TryGetCached(icon.Id, hash) is { } cached)
		{
			return (PluginIconResourceStatus.Found, cached);
		}

		if (sources.Find(WidgetIconReference.IconPackType) is not { } source)
		{
			return (PluginIconResourceStatus.NotFound, null);
		}

		var rendition = await WidgetIconRenditions.ProduceAsync(source, icon.Id.ToString(), cancellationToken);
		switch (rendition.Status)
		{
			case WidgetIconRenditionStatus.Rendered:
				var content = new UiResourceContent
				{
					Content = rendition.Content!,
					MediaType = rendition.MediaType!,
					ContentHash = AssetContentHash.Compute(rendition.Content)
				};
				Store(icon.Id, hash, content);
				return (PluginIconResourceStatus.Found, content);

			case WidgetIconRenditionStatus.TooLarge:
				return (PluginIconResourceStatus.TooLarge, null);

			default:
				return (PluginIconResourceStatus.NotFound, null);
		}
	}

	private UiResourceContent? TryGetCached(Guid iconId, string hash)
	{
		lock (_sync)
		{
			for (var node = _recency.First; node is not null; node = node.Next)
			{
				if (node.Value.IconId != iconId)
				{
					continue;
				}

				if (!string.Equals(node.Value.Hash, hash, StringComparison.Ordinal))
				{
					_recency.Remove(node);
					return null;
				}

				_recency.Remove(node);
				_recency.AddLast(node);
				return node.Value.Content;
			}

			return null;
		}
	}

	private void Store(Guid iconId, string hash, UiResourceContent content)
	{
		lock (_sync)
		{
			for (var node = _recency.First; node is not null; node = node.Next)
			{
				if (node.Value.IconId == iconId)
				{
					_recency.Remove(node);
					break;
				}
			}

			_recency.AddLast((iconId, hash, content));
			while (_recency.Count > MaxEntries)
			{
				_recency.RemoveFirst();
			}
		}
	}
}
