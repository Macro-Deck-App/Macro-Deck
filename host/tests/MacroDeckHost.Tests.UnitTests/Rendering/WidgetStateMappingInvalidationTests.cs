using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class WidgetStateMappingInvalidationTests
{
	private const string BoundOnCpu =
		"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
		"\"stateMapping\":{\"rules\":[{\"id\":\"r\",\"stateId\":\"on\",\"when\":{\"kind\":\"compare\",\"id\":\"c\"," +
		"\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}}],\"fallbackStateId\":\"off\"}}";

	private const string BoundOnLocal =
		"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
		"\"stateMapping\":{\"rules\":[{\"id\":\"r\",\"stateId\":\"on\",\"when\":{\"kind\":\"compare\",\"id\":\"c\"," +
		"\"left\":{\"$var\":\"local\"},\"operator\":\"==\",\"right\":true}}],\"fallbackStateId\":\"off\"}}";

	// Legacy, pre-#612 shape - data that has not been re-saved since the upgrade still carries
	// stateBinding rather than stateMapping. WidgetVariableReferenceParser has to keep invalidating it
	// without waiting for a save to migrate it (WidgetVariableReferenceParser.LegacyStateBindingKey).
	private const string LegacyBoundOnCpu =
		"{\"mode\":\"toggle\",\"stateBinding\":{\"kind\":\"compare\",\"id\":\"c\"," +
		"\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}}";

	private static WidgetEntity Button(Guid id, string? data) => new()
	{
		Id = id,
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		Data = data
	};

	private static VariableEntity Variable(string name, VariableScope scope, string? scopeRefId = null) => new()
	{
		Name = name,
		Scope = scope,
		ScopeRefId = scopeRefId,
		Type = VariableType.Numeric,
		Classification = VariableClassification.User
	};

	private static WidgetVariableIndex IndexOver(params WidgetEntity[] widgets)
	{
		var index = new WidgetVariableIndex(new FakeFolderCache(widgets));
		index.Rebuild();
		return index;
	}

	private static List<Guid> Drain(WidgetStateEvalChannel queue)
	{
		var ids = new List<Guid>();
		while (queue.Reader.TryRead(out var id))
		{
			ids.Add(id);
		}

		return ids;
	}

	[Test]
	public async Task GlobalVariable_EnqueuesOnlyBoundButtons()
	{
		var bound = Button(Guid.NewGuid(), BoundOnCpu);
		var unbound = Button(Guid.NewGuid(), "{\"mode\":\"toggle\"}");
		var queue = new WidgetStateEvalChannel();
		var handler = new VariableValueChangedStateMappingHandler(IndexOver(bound, unbound), queue);

		await handler.Handle(new VariableValueChangedNotification(Variable("cpu", VariableScope.Global), "old"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([bound.Id]));
	}

	[Test]
	public async Task BoundButton_isEnqueued_evenWithoutSubscriber()
	{
		var bound = Button(Guid.NewGuid(), BoundOnCpu);
		var queue = new WidgetStateEvalChannel();
		var handler = new VariableValueChangedStateMappingHandler(IndexOver(bound), queue);

		await handler.Handle(new VariableValueChangedNotification(Variable("cpu", VariableScope.Global), "old"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([bound.Id]));
	}

	[Test]
	public async Task ActionButtonScopedVariable_EnqueuesOnlyOwner()
	{
		var owner = Button(Guid.NewGuid(), BoundOnLocal);
		var unrelated = Button(Guid.NewGuid(), BoundOnLocal);
		var queue = new WidgetStateEvalChannel();
		var handler = new VariableValueChangedStateMappingHandler(IndexOver(owner, unrelated), queue);

		await handler.Handle(new VariableValueChangedNotification(
				Variable("local", VariableScope.Widget, owner.Id.ToString()),
				"old"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([owner.Id]));
	}

	// A rule reading the button's own vars.state is authorable in the mapping editor, and the
	// reconciler writes that variable on every pass. Re-enqueueing on it would alternate the button
	// forever at the debounce interval, so the write must not feed back in. The reconciler's own depth
	// bound cannot catch this: the loop goes back through the queue, so every pass is a fresh
	// top-level reconcile rather than a nested one.
	[TestCase("state")]
	[TestCase("state_label")]
	public async Task AButtonsOwnStateVariable_DoesNotReEnqueueIt(string variableName)
	{
		var selfReferencing = Button(Guid.NewGuid(),
			"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}]," +
			"\"stateMapping\":{\"rules\":[{\"id\":\"r\",\"stateId\":\"b\",\"when\":{\"kind\":\"compare\",\"id\":\"c\"," +
			"\"left\":{\"$var\":\"" +
			variableName +
			"\"},\"operator\":\"==\",\"right\":\"a\"}}]," +
			"\"fallbackStateId\":\"a\"}}");
		var queue = new WidgetStateEvalChannel();
		var handler = new VariableValueChangedStateMappingHandler(IndexOver(selfReferencing), queue);

		await handler.Handle(new VariableValueChangedNotification(
				Variable(variableName, VariableScope.Widget, selfReferencing.Id.ToString()),
				"a"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.Empty);
	}

	[Test]
	public async Task LegacyStateBindingShapedData_StillInvalidates_BeforeItHasBeenReSaved()
	{
		var legacy = Button(Guid.NewGuid(), LegacyBoundOnCpu);
		var queue = new WidgetStateEvalChannel();
		var handler = new VariableValueChangedStateMappingHandler(IndexOver(legacy), queue);

		await handler.Handle(new VariableValueChangedNotification(Variable("cpu", VariableScope.Global), "old"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([legacy.Id]));
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(params WidgetEntity[] widgets)
		{
			_folder = new FolderEntity { Name = "f", Order = 0, Widgets = widgets.ToList() };
		}

		public List<FolderEntity> GetAllFolders() => [_folder];

		public Task InitializeCache() => Task.CompletedTask;
		public FolderEntity? GetFolderById(Guid id) => _folder;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
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
}
