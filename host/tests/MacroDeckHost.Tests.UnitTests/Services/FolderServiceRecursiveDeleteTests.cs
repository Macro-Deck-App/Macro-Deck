using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceRecursiveDeleteTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private FolderService _service = null!;
	private RecordingMediator _mediator = null!;
	private FakeSecretService _secrets = null!;
	private Guid _profileId;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryProfileStore();
		_cache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity
			{ Id = _profileId, Name = "P", DefaultRows = 3, DefaultColumns = 5 });
		_folderCache = new FolderCache(_cache);
		_mediator = new RecordingMediator();
		_secrets = new FakeSecretService();
		_service = new FolderService(_folderCache,
			_cache,
			new InMemoryDeviceRepository(),
			_mediator,
			new WidgetSecretCloner(_secrets),
			new WidgetSecretScrubber(_secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Delete_ThreeLevelSubtreeWithWidgetsAtEveryLevel_RemovesEverythingFromCacheAndPersistedFile()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var child = await AddFolder("Child", root.Id);
		var grandchild = await AddFolder("Grandchild", child.Id);
		AddWidget(root.Id);
		AddWidget(child.Id);
		AddWidget(grandchild.Id);
		var survivor = await AddFolder("Survivor", null);

		var result = await _service.Delete(root.Id);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(_cache.GetFolderById(root.Id), Is.Null);
			Assert.That(_cache.GetFolderById(child.Id), Is.Null);
			Assert.That(_cache.GetFolderById(grandchild.Id), Is.Null);
			Assert.That(_cache.GetFolderById(survivor.Id), Is.Not.Null);

			var persisted = _store.Get(_profileId)!.Folders;
			Assert.That(persisted.Select(f => f.Id), Is.EquivalentTo([survivor.Id]));
		});
	}

	[Test]
	public async Task Delete_PublishesOneWidgetDeletedNotificationPerWidgetAndOneFolderDeletedNotificationPerFolder()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var child = await AddFolder("Child", root.Id);
		var rootWidget = AddWidget(root.Id);
		var childWidget = AddWidget(child.Id);
		await AddFolder("Survivor", null);

		await _service.Delete(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(_mediator.Published.OfType<WidgetDeletedNotification>().Select(n => n.WidgetId),
				Is.EquivalentTo([rootWidget.Id, childWidget.Id]));
			Assert.That(_mediator.Published.OfType<FolderDeletedNotification>().Select(n => n.FolderId),
				Is.EquivalentTo([root.Id, child.Id]));
		});
	}

	[Test]
	public async Task Delete_PublishesFolderDeletionsChildBeforeParent()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var child = await AddFolder("Child", root.Id);
		var grandchild = await AddFolder("Grandchild", child.Id);
		await AddFolder("Survivor", null);

		await _service.Delete(root.Id);

		var order = _mediator.Published.OfType<FolderDeletedNotification>().Select(n => n.FolderId).ToList();
		Assert.That(order, Is.EqualTo(new[] { grandchild.Id, child.Id, root.Id }));
	}

	[Test]
	public async Task Delete_PublishesStartFolderUpdateBeforeWidgetDeletionsAndWidgetDeletionsBeforeFolderDeletions()
	{
		var root = await AddFolder("Root",
			null,
			isDefault: true,
			createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		var replacement = await AddFolder("Replacement",
			null,
			createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		AddWidget(root.Id);

		await _service.Delete(root.Id);

		var published = _mediator.Published;
		var updateIndex = published.FindIndex(n => n is FolderUpdatedNotification);
		var widgetDeleteIndex = published.FindIndex(n => n is WidgetDeletedNotification);
		var folderDeleteIndex = published.FindIndex(n => n is FolderDeletedNotification);

		Assert.Multiple(() =>
		{
			Assert.That(updateIndex, Is.EqualTo(0));
			Assert.That(updateIndex, Is.LessThan(widgetDeleteIndex));
			Assert.That(widgetDeleteIndex, Is.LessThan(folderDeleteIndex));
			Assert.That(((FolderUpdatedNotification)published[updateIndex]).Folder.Id, Is.EqualTo(replacement.Id));
		});
	}

	[Test]
	public async Task Delete_ScrubsSecretsForSubtreeWidgetsButNotSurvivors()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var survivor = await AddFolder("Survivor", null);
		var subtreeSecret = _secrets.Store("subtree-secret");
		var survivingSecret = _secrets.Store("surviving-secret");
		AddWidget(root.Id, $"{{\"$secret\":\"{subtreeSecret}\"}}");
		AddWidget(survivor.Id, $"{{\"$secret\":\"{survivingSecret}\"}}");

		await _service.Delete(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(_secrets.Resolve(subtreeSecret).Result, Is.Null);
			Assert.That(_secrets.Resolve(survivingSecret).Result, Is.EqualTo("surviving-secret"));
		});
	}

	[Test]
	public async Task Delete_CurrentStartFolder_MovesTheMarkerToTheOldestRemainingRoot()
	{
		var root = await AddFolder("Root",
			null,
			isDefault: true,
			createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		var replacement = await AddFolder("Replacement",
			null,
			createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		await AddFolder("Child", root.Id);

		var result = await _service.Delete(root.Id);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(replacement.IsDefault, Is.True);
			Assert.That(_store.Get(_profileId)!.Folders.Single(f => f.Id == replacement.Id).IsDefault, Is.True);
		});
	}

	[Test]
	public async Task Delete_TheOnlyFolder_IsRejectedAsTheLastFolderWithNoNotifications()
	{
		var only = await AddFolder("Only", null, isDefault: true);

		var result = await _service.Delete(only.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.CannotDeleteLastFolder));
			Assert.That(_cache.GetFolderById(only.Id), Is.Not.Null);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task Delete_SubtreeSpanningTheWholeProfile_IsRejectedAsTheLastFolder()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var child = await AddFolder("Child", root.Id);

		var result = await _service.Delete(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.CannotDeleteLastFolder));
			Assert.That(_cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(_cache.GetFolderById(child.Id), Is.Not.Null);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task Delete_OnlyRootStartFolderWithNoValidReplacementRoot_IsRejected()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		// A dangling parent reference (pre-existing data corruption) is not itself a root, so it cannot
		// replace the start folder even though more than one folder exists in the profile.
		await AddFolder("Orphan", Guid.NewGuid());

		var result = await _service.Delete(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.CannotDeleteLastFolder));
			Assert.That(_cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task Delete_Subtree_PersistsWithExactlyOneSave()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var child = await AddFolder("Child", root.Id);
		AddWidget(root.Id);
		AddWidget(child.Id);
		await AddFolder("Survivor", null);
		var savesBefore = _store.SaveCount;

		await _service.Delete(root.Id);

		Assert.That(_store.SaveCount, Is.EqualTo(savesBefore + 1));
	}

	[Test]
	public async Task Delete_PersistFailure_RestoresFoldersAndMarkersAndScrubsNoSecretsAndPublishesNothing()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var child = await AddFolder("Child", root.Id);
		var secretId = _secrets.Store("s");
		AddWidget(child.Id, $"{{\"$secret\":\"{secretId}\"}}");
		var survivor = await AddFolder("Survivor", null);
		var savesBefore = _store.SaveCount;
		_store.FailSaves = true;

		var result = await _service.Delete(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.InternalError));
			Assert.That(_cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(_cache.GetFolderById(child.Id), Is.Not.Null);
			Assert.That(root.IsDefault, Is.True);
			Assert.That(survivor.IsDefault, Is.False);
			Assert.That(_mediator.Published, Is.Empty);
			Assert.That(_secrets.Resolve(secretId).Result, Is.EqualTo("s"));
			Assert.That(_store.SaveCount, Is.EqualTo(savesBefore + 1));
		});
	}

	[Test]
	public async Task Delete_LeafFolder_RemovesOnlyThatFolderAndItsOwnWidgets()
	{
		var root = await AddFolder("Root", null, isDefault: true);
		var leaf = await AddFolder("Leaf", root.Id);
		var widget = AddWidget(leaf.Id);

		var result = await _service.Delete(leaf.Id);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(_cache.GetFolderById(leaf.Id), Is.Null);
			Assert.That(_cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(_mediator.Published.OfType<WidgetDeletedNotification>().Select(n => n.WidgetId),
				Is.EquivalentTo([widget.Id]));
			Assert.That(_mediator.Published.OfType<FolderDeletedNotification>().Select(n => n.FolderId),
				Is.EquivalentTo([leaf.Id]));
		});
	}

	private async Task<FolderEntity> AddFolder(string name,
		Guid? parentId,
		bool isDefault = false,
		DateTime? createdAt = null)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = _profileId,
			Name = name,
			ParentId = parentId,
			Order = _cache.GetFoldersByProfileId(_profileId).Count,
			Rows = 3,
			Columns = 5,
			IsDefault = isDefault,
			CreatedAt = createdAt ?? DateTime.UtcNow
		};
		await _folderCache.AddOrUpdate(folder);
		return folder;
	}

	private WidgetEntity AddWidget(Guid folderId, string? data = null)
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = folderId,
			Type = WidgetTypeIds.ActionButton,
			Data = data
		};
		_folderCache.AddWidget(folderId, widget);
		return widget;
	}
}
