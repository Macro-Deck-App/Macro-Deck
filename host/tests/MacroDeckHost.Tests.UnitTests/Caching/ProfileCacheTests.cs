using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Caching;

[TestFixture]
public class ProfileCacheTests
{
	private static ProfileCache NewCache(InMemoryProfileStore store)
		=> new(store, new LoggerConfiguration().CreateLogger());

	[Test]
	public async Task InitializeCache_LoadsProfilesAndFolders()
	{
		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "Default",
			Folders =
			[
				new ProfileFolder
				{
					Id = folderId,
					Name = "Main",
					Rows = 3,
					Columns = 5,
					WidgetSpacing = 6,
					WidgetBorderRadius = 24
				}
			]
		});
		var cache = NewCache(store);

		await cache.InitializeCache();

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetAll().Select(p => p.Id), Is.EquivalentTo([profileId]));
			Assert.That(cache.GetFolderById(folderId), Is.Not.Null);
			Assert.That(cache.GetFolderById(folderId)!.ProfileId, Is.EqualTo(profileId));
			Assert.That(cache.GetFolderById(folderId)!.WidgetSpacing, Is.EqualTo(6));
			Assert.That(cache.GetFolderById(folderId)!.WidgetBorderRadius, Is.EqualTo(24));
			Assert.That(cache.GetFoldersByProfileId(profileId), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task InitializeCache_RepairsInvalidStartMarkersWithTheOldestRoot()
	{
		var profileId = Guid.NewGuid();
		var oldestRootId = Guid.NewGuid();
		var newerRootId = Guid.NewGuid();
		var childId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "Legacy",
			Folders =
			[
				new ProfileFolder
				{
					Id = oldestRootId, Name = "Old", Order = 4, Rows = 3, Columns = 5,
					IsDefault = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
				},
				new ProfileFolder
				{
					Id = newerRootId, Name = "New", Order = 0, Rows = 3, Columns = 5,
					IsDefault = true, CreatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
				},
				new ProfileFolder
				{
					Id = childId, Name = "Child", ParentId = oldestRootId, Order = 0, Rows = 3, Columns = 5,
					IsDefault = true, CreatedAt = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)
				}
			]
		});
		var cache = NewCache(store);

		await cache.InitializeCache();

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetFoldersByProfileId(profileId).Single(folder => folder.Id == oldestRootId).IsDefault,
				Is.True);
			Assert.That(cache.GetFoldersByProfileId(profileId).Where(folder => folder.Id != oldestRootId)
					.All(folder => !folder.IsDefault),
				Is.True);
			Assert.That(store.Get(profileId)!.Folders.Single(folder => folder.Id == oldestRootId).IsDefault, Is.True);
			Assert.That(store.Get(profileId)!.Folders.Where(folder => folder.Id != oldestRootId)
					.All(folder => !folder.IsDefault),
				Is.True);
		});

		cache.Dispose();
	}

	[Test]
	public async Task InitializeCache_RepairsNoMarkerUsingLegacyRootOrderThenId()
	{
		var profileId = Guid.NewGuid();
		var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
		var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "Legacy",
			Folders =
			[
				new ProfileFolder { Id = secondId, Name = "Second", Order = 0, Rows = 3, Columns = 5 },
				new ProfileFolder { Id = firstId, Name = "First", Order = 0, Rows = 3, Columns = 5 }
			]
		});
		var cache = NewCache(store);

		await cache.InitializeCache();

		Assert.That(cache.GetFolderById(firstId)!.IsDefault, Is.True);
		Assert.That(cache.GetFolderById(secondId)!.IsDefault, Is.False);

		cache.Dispose();
	}

	[Test]
	public async Task AddOrUpdateFolder_PersistsWholeProfile()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();

		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = 3,
			Columns = 5,
			WidgetSpacing = 6,
			WidgetBorderRadius = 24
		});

		var saved = store.Get(profileId);
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.Folders, Has.Count.EqualTo(1));
		Assert.That(saved.Folders[0].WidgetSpacing, Is.EqualTo(6));
		Assert.That(saved.Folders[0].WidgetBorderRadius, Is.EqualTo(24));
	}

	[Test]
	public async Task AddOrUpdateFolders_PersistsOnceAndNormalizesTheStartFolderBeforePersisting()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();

		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });

		var older = new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = profileId, Name = "Older", Order = 0, Rows = 3, Columns = 5,
			CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
		};
		var newer = new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = profileId, Name = "Newer", Order = 1, Rows = 3, Columns = 5,
			CreatedAt = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)
		};
		var savesBefore = store.SaveCount;

		await cache.AddOrUpdateFolders([older, newer]);

		Assert.Multiple(() =>
		{
			Assert.That(store.SaveCount, Is.EqualTo(savesBefore + 1));
			Assert.That(older.IsDefault, Is.True);
			Assert.That(newer.IsDefault, Is.False);
			Assert.That(store.Get(profileId)!.Folders.Single(f => f.Id == older.Id).IsDefault, Is.True);
		});

		cache.Dispose();
	}

	[Test]
	public async Task AddWidget_PersistsWidgetIntoOwningProfile()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();

		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = folderId,
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = 3,
			Columns = 5
		});

		cache.AddWidget(folderId,
			new WidgetEntity { Id = Guid.NewGuid(), FolderId = folderId, Type = WidgetTypeIds.Slider });

		var saved = store.Get(profileId);
		Assert.That(saved!.Folders[0].Widgets, Has.Count.EqualTo(1));
		Assert.That(saved.Folders[0].Widgets[0].Type, Is.EqualTo(WidgetTypeIds.Slider));
	}

	[Test]
	public async Task UpdateWidgetPositions_AppliesAllPlacementsWithASinglePersist()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();

		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = folderId,
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = 4,
			Columns = 4
		});

		var widgetA = new WidgetEntity { Id = Guid.NewGuid(), FolderId = folderId, Type = WidgetTypeIds.ActionButton };
		var widgetB = new WidgetEntity { Id = Guid.NewGuid(), FolderId = folderId, Type = WidgetTypeIds.ActionButton };
		cache.AddWidget(folderId, widgetA);
		cache.AddWidget(folderId, widgetB);
		var savesBefore = store.SaveCount;

		cache.UpdateWidgetPositions(folderId,
		[
			new WidgetPlacement(widgetA.Id, 2, 3, 1, 1),
			new WidgetPlacement(widgetB.Id, 0, 1, 2, 2)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(store.SaveCount, Is.EqualTo(savesBefore + 1));
			Assert.That(widgetA.PositionX, Is.EqualTo(2));
			Assert.That(widgetA.PositionY, Is.EqualTo(3));
			Assert.That(widgetB.Width, Is.EqualTo(2));
			Assert.That(widgetB.Height, Is.EqualTo(2));
			var savedWidgets = store.Get(profileId)!.Folders[0].Widgets;
			Assert.That(savedWidgets.Single(w => w.Id == widgetA.Id).PositionX, Is.EqualTo(2));
			Assert.That(savedWidgets.Single(w => w.Id == widgetB.Id).Width, Is.EqualTo(2));
		});

		cache.Dispose();
	}

	[Test]
	public async Task Remove_DeletesProfileAndItsFolders()
	{
		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		var store = new InMemoryProfileStore(new ProfileFile
		{
			Id = profileId,
			Name = "P",
			Folders = [new ProfileFolder { Id = folderId, Name = "F" }]
		});
		var cache = NewCache(store);
		await cache.InitializeCache();

		await cache.Remove(profileId);

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetById(profileId), Is.Null);
			Assert.That(cache.GetFolderById(folderId), Is.Null);
			Assert.That(store.Get(profileId), Is.Null);
		});
	}

	[Test]
	public async Task RemoveFolderSubtree_RemovesRootAndDescendantsInOneWrite()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var root = await AddFolder(cache, profileId, "Root", null, isDefault: true);
		var child = await AddFolder(cache, profileId, "Child", root.Id);
		var survivor = await AddFolder(cache, profileId, "Survivor", null);
		var savesBefore = store.SaveCount;

		var removal = await cache.RemoveFolderSubtree(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(removal.Persisted, Is.True);
			Assert.That(removal.RemovedFolders.Select(f => f.Id), Is.EqualTo(new[] { root.Id, child.Id }));
			Assert.That(cache.GetFolderById(root.Id), Is.Null);
			Assert.That(cache.GetFolderById(child.Id), Is.Null);
			Assert.That(cache.GetFolderById(survivor.Id), Is.Not.Null);
			Assert.That(store.SaveCount, Is.EqualTo(savesBefore + 1));
			Assert.That(store.Get(profileId)!.Folders.Select(f => f.Id), Is.EquivalentTo([survivor.Id]));
		});

		cache.Dispose();
	}

	[Test]
	public async Task RemoveFolderSubtree_ResolvesTheSubtreeFromCurrentCacheStateNotAStaleList()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var root = await AddFolder(cache, profileId, "Root", null, isDefault: true);
		await AddFolder(cache, profileId, "Survivor", null);

		var lateChild = await AddFolder(cache, profileId, "LateChild", root.Id);

		var removal = await cache.RemoveFolderSubtree(root.Id);

		Assert.That(removal.RemovedFolders.Select(f => f.Id), Is.EquivalentTo(new[] { root.Id, lateChild.Id }));
		Assert.That(cache.GetFolderById(lateChild.Id), Is.Null);

		cache.Dispose();
	}

	[Test]
	public async Task RemoveFolderSubtree_ReassignsTheStartMarkerToTheOldestSurvivingRoot()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var root = await AddFolder(cache,
			profileId,
			"Root",
			null,
			isDefault: true,
			createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		var replacement = await AddFolder(cache,
			profileId,
			"Replacement",
			null,
			createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

		var removal = await cache.RemoveFolderSubtree(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(removal.ChangedStartFolders.Select(f => f.Id), Is.EqualTo(new[] { replacement.Id }));
			Assert.That(replacement.IsDefault, Is.True);
			Assert.That(store.Get(profileId)!.Folders.Single(f => f.Id == replacement.Id).IsDefault, Is.True);
		});

		cache.Dispose();
	}

	[Test]
	public async Task RemoveFolderSubtree_UnknownRootId_ReturnsNotPersistedWithNoChanges()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var savesBefore = store.SaveCount;

		var removal = await cache.RemoveFolderSubtree(Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(removal.Persisted, Is.False);
			Assert.That(removal.Failure, Is.EqualTo(FolderSubtreeRemovalFailure.RootNotFound));
			Assert.That(removal.RemovedFolders, Is.Empty);
			Assert.That(store.SaveCount, Is.EqualTo(savesBefore));
		});

		cache.Dispose();
	}

	[Test]
	public async Task RemoveFolderSubtree_SubtreeIsWholeProfile_ReturnsNotPersistedWithLastFolderReason()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var root = await AddFolder(cache, profileId, "Root", null, isDefault: true);
		var child = await AddFolder(cache, profileId, "Child", root.Id);

		var removal = await cache.RemoveFolderSubtree(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(removal.Persisted, Is.False);
			Assert.That(removal.Failure, Is.EqualTo(FolderSubtreeRemovalFailure.LastFolder));
			Assert.That(removal.RemovedFolders, Is.Empty);
			Assert.That(cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(cache.GetFolderById(child.Id), Is.Not.Null);
		});

		cache.Dispose();
	}

	[Test]
	public async Task RemoveFolderSubtree_OnlyRootStartFolderWithNoSurvivingRoot_ReturnsNotPersistedWithReason()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var root = await AddFolder(cache, profileId, "Root", null, isDefault: true);
		var orphan = await AddFolder(cache, profileId, "Orphan", Guid.NewGuid());

		var removal = await cache.RemoveFolderSubtree(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(removal.Persisted, Is.False);
			Assert.That(removal.Failure, Is.EqualTo(FolderSubtreeRemovalFailure.OnlyRootStartFolder));
			Assert.That(removal.RemovedFolders, Is.Empty);
			Assert.That(cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(cache.GetFolderById(orphan.Id), Is.Not.Null);
		});

		cache.Dispose();
	}

	[Test]
	public async Task RemoveFolderSubtree_PersistFailure_RestoresFoldersAndMarkers()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		var root = await AddFolder(cache, profileId, "Root", null, isDefault: true);
		var child = await AddFolder(cache, profileId, "Child", root.Id);
		var survivor = await AddFolder(cache, profileId, "Survivor", null);
		store.FailSaves = true;

		var removal = await cache.RemoveFolderSubtree(root.Id);

		Assert.Multiple(() =>
		{
			Assert.That(removal.Persisted, Is.False);
			Assert.That(removal.Failure, Is.EqualTo(FolderSubtreeRemovalFailure.PersistFailed));
			Assert.That(removal.RemovedFolders, Is.Empty);
			Assert.That(cache.GetFolderById(root.Id), Is.Not.Null);
			Assert.That(cache.GetFolderById(child.Id), Is.Not.Null);
			Assert.That(root.IsDefault, Is.True);
			Assert.That(survivor.IsDefault, Is.False);
		});

		cache.Dispose();
	}

	[Test]
	public async Task WidgetMutators_StillBehaveCorrectlyNowThatTheyTakeTheUpdateLock()
	{
		var store = new InMemoryProfileStore();
		var cache = NewCache(store);
		await cache.InitializeCache();
		var profileId = Guid.NewGuid();
		var folderId = Guid.NewGuid();
		await cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = folderId, ProfileId = profileId, Name = "F", Order = 0, Rows = 3, Columns = 5
		});

		var widget = new WidgetEntity { Id = Guid.NewGuid(), FolderId = folderId, Type = WidgetTypeIds.ActionButton };
		cache.AddWidget(folderId, widget);
		cache.UpdateWidget(folderId,
			new WidgetEntity
			{
				Id = widget.Id, FolderId = folderId, Type = WidgetTypeIds.Slider
			});
		cache.UpdateWidgetPositions(folderId, [new WidgetPlacement(widget.Id, 1, 1, 2, 2)]);

		Assert.Multiple(() =>
		{
			var stored = cache.GetFolderById(folderId)!.Widgets.Single();
			Assert.That(stored.Type, Is.EqualTo(WidgetTypeIds.Slider));
			Assert.That(stored.PositionX, Is.EqualTo(1));
			Assert.That(stored.Width, Is.EqualTo(2));
		});

		cache.RemoveWidget(folderId, widget.Id);

		Assert.That(cache.GetFolderById(folderId)!.Widgets, Is.Empty);

		cache.Dispose();
	}

	private static async Task<FolderEntity> AddFolder(ProfileCache cache,
		Guid profileId,
		string name,
		Guid? parentId,
		bool isDefault = false,
		DateTime? createdAt = null)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profileId,
			Name = name,
			ParentId = parentId,
			Order = cache.GetFoldersByProfileId(profileId).Count,
			Rows = 3,
			Columns = 5,
			IsDefault = isDefault,
			CreatedAt = createdAt ?? DateTime.UtcNow
		};
		await cache.AddOrUpdateFolder(folder);
		return folder;
	}
}
