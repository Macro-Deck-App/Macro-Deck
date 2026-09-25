using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconUsageScannerTests
{
	[Test]
	public void Icons_named_by_a_widget_an_automation_or_a_script_are_all_in_use()
	{
		var onButton = Guid.NewGuid();
		var setByAutomation = Guid.NewGuid();
		var setByScript = Guid.NewGuid();
		var folders = new FolderCacheWith(new FolderEntity
		{
			Name = "Home",
			Order = 0,
			Widgets = [new WidgetEntity { Data = $"{{\"icon\":{{\"type\":\"icon-pack\",\"reference\":\"{onButton}\"}}}}" }]
		});
		var automations = new StubAutomationCache();
		automations.Add($"[{{\"action\":\"set-icon\",\"parameters\":{{\"icon\":\"{setByAutomation}\"}}}}]");
		var scripts = new ScriptCacheWith(new ScriptEntity
		{
			Name = "Script",
			Flows = $"[{{\"action\":\"set-icon\",\"parameters\":{{\"icon\":\"{setByScript}\"}}}}]"
		});

		var inUse = new IconUsageScanner(folders, automations, scripts).FindReferencedIconIds();

		Assert.That(inUse, Is.SupersetOf(new[] { onButton, setByAutomation, setByScript }));
	}

	private sealed class FolderCacheWith(params FolderEntity[] folders) : IFolderCache
	{
		public List<FolderEntity> GetAllFolders() => folders.ToList();

		public Task InitializeCache() => throw new NotSupportedException();

		public FolderEntity? GetFolderById(Guid id) => throw new NotSupportedException();

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => throw new NotSupportedException();

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => throw new NotSupportedException();

		public Task AddOrUpdate(FolderEntity folder) => throw new NotSupportedException();

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => throw new NotSupportedException();

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId) => throw new NotSupportedException();

		public void AddWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => throw new NotSupportedException();

		public void UpdateWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => throw new NotSupportedException();

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public void RemoveWidget(Guid folderId, Guid widgetId) => throw new NotSupportedException();

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds) => throw new NotSupportedException();

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
			=> throw new NotSupportedException();
	}

	private sealed class ScriptCacheWith(params ScriptEntity[] scripts) : IScriptCache
	{
		public List<ScriptEntity> GetAll() => scripts.ToList();

		public Task InitializeCache() => throw new NotSupportedException();

		public ScriptEntity? GetById(Guid id) => throw new NotSupportedException();

		public Task AddOrUpdate(ScriptEntity script) => throw new NotSupportedException();

		public Task Remove(Guid id) => throw new NotSupportedException();
	}
}
