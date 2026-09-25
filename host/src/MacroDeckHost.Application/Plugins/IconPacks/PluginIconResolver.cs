using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public interface IPluginIconResolver
{
	IconEntity? Resolve(string pluginId, string key, string name);

	IconEntity? Resolve(string pluginId, string? reference);
}

// The calling plugin's id and its current declaration are part of every lookup, so a reference only reaches
// a pack that plugin declares now. Synchronous and cached: this runs inside relay paths.
public sealed class PluginIconResolver(IIconPackCache iconPackCache, IPluginBundledIconPackDeclarations declarations)
	: IPluginIconResolver
{
	public IconEntity? Resolve(string pluginId, string key, string name)
	{
		if (declarations.Find(pluginId)?.Declares(key) != true)
		{
			return null;
		}

		var sourceId = PluginIconReferences.SourceId(pluginId, key);
		var pack = iconPackCache.GetAllPacks()
			.Where(candidate => candidate.SourceType == IconPackSourceType.Plugin &&
				string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal))
			.OrderBy(candidate => candidate.CreatedAt)
			.FirstOrDefault();

		return pack is null
			? null
			: iconPackCache.GetIconsByPackId(pack.Id)
				.Where(icon => icon.ProcessingState == IconProcessingState.Ready &&
					string.Equals(icon.Name, name, StringComparison.OrdinalIgnoreCase))
				.OrderBy(icon => icon.CreatedAt)
				.FirstOrDefault();
	}

	public IconEntity? Resolve(string pluginId, string? reference)
		=> PluginIconReferences.TryParse(reference, out var key, out var name) ? Resolve(pluginId, key, name) : null;
}
