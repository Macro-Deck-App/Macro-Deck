using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

internal sealed class StubFolderCache : IFolderCache
{
	private readonly List<FolderEntity> _folders = [];

	public FolderEntity AddFolder(params WidgetEntity[] widgets)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = Guid.NewGuid(),
			Name = "Folder",
			Order = 0,
			Widgets = widgets.ToList()
		};

		_folders.Add(folder);
		return folder;
	}

	public void RemoveFolder(Guid folderId) => _folders.RemoveAll(f => f.Id == folderId);

	public static WidgetEntity Widget(string? data) => new()
	{
		Id = Guid.NewGuid(),
		FolderId = Guid.Empty,
		Type = WidgetTypeIds.ActionButton,
		Data = data
	};

	public Task InitializeCache() => Task.CompletedTask;

	public FolderEntity? GetFolderById(Guid id) => _folders.FirstOrDefault(f => f.Id == id);

	public List<FolderEntity> GetAllFolders() => _folders.ToList();

	public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [];

	public List<FolderEntity> GetFoldersByProfileId(Guid profileId)
		=> _folders.Where(f => f.ProfileId == profileId).ToList();

	public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;

	public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

	public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
		=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

	public void AddWidget(Guid folderId, WidgetEntity widget)
	{
	}

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
	}

	public void UpdateWidget(Guid folderId, WidgetEntity widget)
	{
	}

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
	}

	public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
	{
	}

	public void RemoveWidget(Guid folderId, Guid widgetId)
	{
	}

	public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
	{
	}

	public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
	{
	}
}
