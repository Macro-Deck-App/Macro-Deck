using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Caching;

public interface IFolderCache
{
	Task InitializeCache();

	FolderEntity? GetFolderById(Guid id);

	List<FolderEntity> GetAllFolders();

	List<FolderEntity> GetFoldersByParentId(Guid? parentId);

	List<FolderEntity> GetFoldersByProfileId(Guid profileId);

	Task AddOrUpdate(FolderEntity folder);

	Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders);

	Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId);

	void AddWidget(Guid folderId, WidgetEntity widget);

	void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets);

	void UpdateWidget(Guid folderId, WidgetEntity widget);

	void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets);

	void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements);

	void RemoveWidget(Guid folderId, Guid widgetId);

	void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds);

	void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets);

	Task AddOrUpdate(FolderEntity folder, SecretApprovalWrite write)
	{
		return AddOrUpdate(folder);
	}

	Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders, SecretApprovalWrite write)
	{
		return AddOrUpdateRange(folders);
	}

	void AddWidget(Guid folderId, WidgetEntity widget, SecretApprovalWrite write)
	{
		AddWidget(folderId, widget);
	}

	void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets, SecretApprovalWrite write)
	{
		AddWidgets(folderId, widgets);
	}

	void UpdateWidget(Guid folderId, WidgetEntity widget, SecretApprovalWrite write)
	{
		UpdateWidget(folderId, widget);
	}

	void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets, SecretApprovalWrite write)
	{
		UpdateWidgets(folderId, widgets);
	}

	void ReplaceWidgets(
		Guid folderId,
		IReadOnlyList<Guid> removeIds,
		IReadOnlyList<WidgetEntity> addWidgets,
		SecretApprovalWrite write)
	{
		ReplaceWidgets(folderId, removeIds, addWidgets);
	}
}
