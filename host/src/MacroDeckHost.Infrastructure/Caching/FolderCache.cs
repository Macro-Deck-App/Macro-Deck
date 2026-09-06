using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Infrastructure.Caching;

public sealed class FolderCache : IFolderCache
{
	private readonly ProfileCache _profileCache;

	public FolderCache(ProfileCache profileCache)
	{
		_profileCache = profileCache;
	}

	public Task InitializeCache() => _profileCache.InitializeCache();

	public FolderEntity? GetFolderById(Guid id) => _profileCache.GetFolderById(id);

	public List<FolderEntity> GetAllFolders() => _profileCache.GetAllFolders();

	public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => _profileCache.GetFoldersByParentId(parentId);

	public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => _profileCache.GetFoldersByProfileId(profileId);

	public Task AddOrUpdate(FolderEntity folder) => _profileCache.AddOrUpdateFolder(folder);

	public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) =>
		_profileCache.AddOrUpdateFolders(folders);

	public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId) => _profileCache.RemoveFolderSubtree(rootId);

	public void AddWidget(Guid folderId, WidgetEntity widget) => _profileCache.AddWidget(folderId, widget);

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		=> _profileCache.AddWidgets(folderId, widgets);

	public void UpdateWidget(Guid folderId, WidgetEntity widget) => _profileCache.UpdateWidget(folderId, widget);

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		=> _profileCache.UpdateWidgets(folderId, widgets);

	public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		=> _profileCache.UpdateWidgetPositions(folderId, placements);

	public void RemoveWidget(Guid folderId, Guid widgetId) => _profileCache.RemoveWidget(folderId, widgetId);

	public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		=> _profileCache.RemoveWidgets(folderId, widgetIds);

	public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		=> _profileCache.ReplaceWidgets(folderId, removeIds, addWidgets);
}
