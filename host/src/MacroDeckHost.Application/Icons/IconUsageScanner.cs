using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Application.Icons;

public interface IIconUsageScanner
{
	IReadOnlySet<Guid> FindReferencedIconIds();
}

public sealed class IconUsageScanner(
	IFolderCache folderCache,
	IAutomationCache automationCache,
	IScriptCache scriptCache) : IIconUsageScanner
{
	public IReadOnlySet<Guid> FindReferencedIconIds()
	{
		var ids = new HashSet<Guid>();

		foreach (var folder in folderCache.GetAllFolders())
		{
			ids.UnionWith(GuidReferences.ExtractAll(folder.ViewConfiguration));
			foreach (var widget in folder.Widgets)
			{
				ids.UnionWith(GuidReferences.ExtractAll(widget.Data));
			}
		}

		foreach (var automation in automationCache.GetAll())
		{
			ids.UnionWith(GuidReferences.ExtractAll(automation.Flows));
		}

		foreach (var script in scriptCache.GetAll())
		{
			ids.UnionWith(GuidReferences.ExtractAll(script.Flows));
		}

		return ids;
	}
}
