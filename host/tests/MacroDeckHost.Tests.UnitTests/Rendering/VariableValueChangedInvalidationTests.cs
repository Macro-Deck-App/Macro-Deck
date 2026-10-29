using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class VariableValueChangedInvalidationTests
{
	private static WidgetEntity Button(Guid id, string? data)
	{
		return new WidgetEntity
		{
			Id = id,
			FolderId = Guid.NewGuid(),
			Type = WidgetTypeIds.ActionButton,
			Data = data
		};
	}

	private static VariableEntity Variable(string name, VariableScope scope, string? scopeRefId = null)
	{
		return new VariableEntity
		{
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = VariableType.Text,
			Classification = VariableClassification.User
		};
	}

	private static List<Guid> Drain(LabelRenderChannel queue)
	{
		var ids = new List<Guid>();
		while (queue.Reader.TryRead(out var id))
		{
			ids.Add(id);
		}

		return ids;
	}

	private static WidgetVariableIndex IndexOver(params WidgetEntity[] widgets)
	{
		var index = new WidgetVariableIndex(new FakeFolderCache(widgets));
		index.Rebuild();
		return index;
	}

	[Test]
	public async Task GlobalVariable_EnqueuesOnlyReferencingButtons()
	{
		var referencing = Button(Guid.NewGuid(), "{\"label\":\"{{ vars.cpu }}\"}");
		var other = Button(Guid.NewGuid(), "{\"label\":\"static\"}");
		var queue = new LabelRenderChannel();
		var subscriptions = new LabelSubscriptionTracker();
		subscriptions.Add("conn1", referencing.Id.ToString(), "off");
		var handler = new VariableValueChangedNotificationHandler(IndexOver(referencing, other),
			queue,
			subscriptions,
			new RecordingRenderSignals());

		await handler.Handle(new VariableValueChangedNotification(Variable("cpu", VariableScope.Global), "old"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([referencing.Id]));
	}

	[Test]
	public async Task ActionButtonScopedVariable_EnqueuesOnlyOwner()
	{
		var owner = Button(Guid.NewGuid(), "{\"label\":\"{{ vars.local }}\"}");
		var unrelated = Button(Guid.NewGuid(), "{\"label\":\"{{ vars.local }}\"}");
		var queue = new LabelRenderChannel();
		var subscriptions = new LabelSubscriptionTracker();
		subscriptions.Add("conn1", owner.Id.ToString(), "off");
		subscriptions.Add("conn1", unrelated.Id.ToString(), "off");
		var handler = new VariableValueChangedNotificationHandler(IndexOver(owner, unrelated),
			queue,
			subscriptions,
			new RecordingRenderSignals());

		await handler.Handle(new VariableValueChangedNotification(
				Variable("local", VariableScope.Widget, owner.Id.ToString()),
				"old"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([owner.Id]));
	}

	[Test]
	public async Task CreatingOrDeletingAReferencedVariable_RerendersTheLabelsThatShowIt()
	{
		var referencing = Button(Guid.NewGuid(), "{\"label\":\"{{ vars.foo }}\"}");
		var other = Button(Guid.NewGuid(), "{\"label\":\"static\"}");
		var queue = new LabelRenderChannel();
		var subscriptions = new LabelSubscriptionTracker();
		subscriptions.Add("conn1", referencing.Id.ToString(), "off");
		subscriptions.Add("conn1", other.Id.ToString(), "off");
		var signals = new RecordingRenderSignals();
		var handler = new VariableValueChangedNotificationHandler(IndexOver(referencing, other),
			queue,
			subscriptions,
			signals);

		await handler.Handle(new VariableCreatedNotification(Variable("foo", VariableScope.Global)),
			CancellationToken.None);
		var afterCreate = Drain(queue);
		await handler.Handle(new VariableDeletedNotification(Variable("foo", VariableScope.Global)),
			CancellationToken.None);
		var afterDelete = Drain(queue);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(afterCreate, Is.EquivalentTo([referencing.Id]));
			Assert.That(afterDelete, Is.EquivalentTo([referencing.Id]));
			Assert.That(signals.VariableChanges, Is.EqualTo(2));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task RenamingAVariable_RerendersLabelsShowingTheOldAndTheNewName()
	{
		var showsOld = Button(Guid.NewGuid(), "{\"label\":\"{{ vars.foo }}\"}");
		var showsNew = Button(Guid.NewGuid(), "{\"label\":\"{{ vars.bar }}\"}");
		var queue = new LabelRenderChannel();
		var subscriptions = new LabelSubscriptionTracker();
		subscriptions.Add("conn1", showsOld.Id.ToString(), "off");
		subscriptions.Add("conn1", showsNew.Id.ToString(), "off");
		var handler = new VariableValueChangedNotificationHandler(IndexOver(showsOld, showsNew),
			queue,
			subscriptions,
			new RecordingRenderSignals());

		await handler.Handle(new VariableUpdatedNotification(Variable("bar", VariableScope.Global), "foo"),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([showsOld.Id, showsNew.Id]));
	}

	[Test]
	public async Task CreatingAButtonsStateVariable_RerendersItsLabelShowingTheState()
	{
		var owner = Button(Guid.NewGuid(), "{\"label\":\"A {{ vars.state }}\"}");
		var queue = new LabelRenderChannel();
		var subscriptions = new LabelSubscriptionTracker();
		subscriptions.Add("conn1", owner.Id.ToString(), "off");
		var handler = new VariableValueChangedNotificationHandler(IndexOver(owner),
			queue,
			subscriptions,
			new RecordingRenderSignals());

		await handler.Handle(new VariableCreatedNotification(
				Variable("state", VariableScope.Widget, owner.Id.ToString())),
			CancellationToken.None);

		Assert.That(Drain(queue), Is.EquivalentTo([owner.Id]));
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(params WidgetEntity[] widgets)
		{
			_folder = new FolderEntity { Name = "f", Order = 0, Widgets = widgets.ToList() };
		}

		public List<FolderEntity> GetAllFolders()
		{
			return [_folder];
		}

		public Task InitializeCache()
		{
			return Task.CompletedTask;
		}

		public FolderEntity? GetFolderById(Guid id)
		{
			return _folder;
		}

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId)
		{
			return [_folder];
		}

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId)
		{
			return [_folder];
		}

		public Task AddOrUpdate(FolderEntity folder)
		{
			return Task.CompletedTask;
		}

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders)
		{
			return Task.CompletedTask;
		}

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
		{
			return Task.FromResult(new FolderSubtreeRemoval(false, [], []));
		}

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
