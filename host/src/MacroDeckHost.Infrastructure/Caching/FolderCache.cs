using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Secrets.Approvals;
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

	public Task InitializeCache()
	{
		return _profileCache.InitializeCache();
	}

	public FolderEntity? GetFolderById(Guid id)
	{
		return _profileCache.GetFolderById(id);
	}

	public List<FolderEntity> GetAllFolders()
	{
		return _profileCache.GetAllFolders();
	}

	public List<FolderEntity> GetFoldersByParentId(Guid? parentId)
	{
		return _profileCache.GetFoldersByParentId(parentId);
	}

	public List<FolderEntity> GetFoldersByProfileId(Guid profileId)
	{
		return _profileCache.GetFoldersByProfileId(profileId);
	}

	public Task AddOrUpdate(FolderEntity folder)
	{
		return _profileCache.AddOrUpdateFolder(folder);
	}

	public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders)
	{
		return _profileCache.AddOrUpdateFolders(folders);
	}

	public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
	{
		return _profileCache.RemoveFolderSubtree(rootId);
	}

	public void AddWidget(Guid folderId, WidgetEntity widget)
	{
		_profileCache.AddWidget(folderId, widget);
	}

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
		_profileCache.AddWidgets(folderId, widgets);
	}

	public void UpdateWidget(Guid folderId, WidgetEntity widget)
	{
		_profileCache.UpdateWidget(folderId, widget);
	}

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
		_profileCache.UpdateWidgets(folderId, widgets);
	}

	public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
	{
		_profileCache.UpdateWidgetPositions(folderId, placements);
	}

	public void RemoveWidget(Guid folderId, Guid widgetId)
	{
		_profileCache.RemoveWidget(folderId, widgetId);
	}

	public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
	{
		_profileCache.RemoveWidgets(folderId, widgetIds);
	}

	public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
	{
		_profileCache.ReplaceWidgets(folderId, removeIds, addWidgets);
	}

	public Task AddOrUpdate(FolderEntity folder, SecretApprovalWrite write)
	{
		return _profileCache.AddOrUpdateFolder(folder, write);
	}

	public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders, SecretApprovalWrite write)
	{
		return _profileCache.AddOrUpdateFolders(folders, write);
	}

	public void AddWidget(Guid folderId, WidgetEntity widget, SecretApprovalWrite write)
	{
		_profileCache.AddWidget(folderId, widget, write);
	}

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets, SecretApprovalWrite write)
	{
		_profileCache.AddWidgets(folderId, widgets, write);
	}

	public void UpdateWidget(Guid folderId, WidgetEntity widget, SecretApprovalWrite write)
	{
		_profileCache.UpdateWidget(folderId, widget, write);
	}

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets, SecretApprovalWrite write)
	{
		_profileCache.UpdateWidgets(folderId, widgets, write);
	}

	public void ReplaceWidgets(
		Guid folderId,
		IReadOnlyList<Guid> removeIds,
		IReadOnlyList<WidgetEntity> addWidgets,
		SecretApprovalWrite write)
	{
		_profileCache.ReplaceWidgets(folderId, removeIds, addWidgets, write);
	}
}
