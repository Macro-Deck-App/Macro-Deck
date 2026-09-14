using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class ThrowingConfigFlowManager : IConfigFlowManager
{
	public Task<ConfigFlowStartOutcome> StartAsync(string integrationId, CancellationToken cancellationToken)
		=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

	public Task<ConfigFlowStartOutcome> StartAsync(string integrationId,
		string? title,
		Guid? entryId,
		CancellationToken cancellationToken)
		=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

	public bool TryGetActiveFlow(Guid flowId, out ConfigFlowActiveFlowInfo? info)
		=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

	public Task<ConfigFlowSubmitOutcome> SubmitAsync(Guid flowId,
		string stepId,
		IReadOnlyDictionary<string, JsonElement> values,
		CancellationToken cancellationToken)
		=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

	public Task<ConfigFlowSubmitOutcome> SubmitAsync(Guid flowId,
		string stepId,
		IReadOnlyDictionary<string, JsonElement> values,
		IReadOnlyCollection<string> clearedSecretFields,
		CancellationToken cancellationToken)
		=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");

	public Task AbandonAsync(Guid flowId, CancellationToken cancellationToken)
		=> throw new InvalidOperationException("widget-config must never touch the config flow manager.");
}

internal sealed class FakeFolderCache : IFolderCache
{
	private readonly FolderEntity _folder = new() { Id = Guid.NewGuid(), Name = "folder", Order = 0 };

	public void AddWidget(WidgetEntity widget) => _folder.Widgets.Add(widget);

	public List<FolderEntity> GetAllFolders() => [_folder];
	public FolderEntity? GetFolderById(Guid id) => id == _folder.Id ? _folder : null;
	public Task InitializeCache() => Task.CompletedTask;
	public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
	public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
	public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;
	public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

	public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
		=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

	public void AddWidget(Guid folderId, WidgetEntity widget) => _folder.Widgets.Add(widget);

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => _folder.Widgets.AddRange(widgets);

	public void UpdateWidget(Guid folderId, WidgetEntity widget)
	{
	}

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
	}

	public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
	{
	}

	public void RemoveWidget(Guid folderId, Guid widgetId) => _folder.Widgets.RemoveAll(w => w.Id == widgetId);

	public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
	{
	}

	public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
	{
	}
}
