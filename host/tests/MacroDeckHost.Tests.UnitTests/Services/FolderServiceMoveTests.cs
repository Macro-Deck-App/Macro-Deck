using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceMoveTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _cache = null!;
	private FolderService _service = null!;
	private RecordingMediator _mediator = null!;
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
		_mediator = new RecordingMediator();
		var secrets = new FakeSecretService();
		_service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			_mediator,
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Move_ReorderSiblingBefore_RenumbersDestinationDensely()
	{
		var a = await AddRoot("A", order: 0, isDefault: true);
		var b = await AddRoot("B", order: 1);
		var c = await AddRoot("C", order: 2);

		var result = await _service.Move(b.Id, a.Id, FolderMovePosition.Before);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(b.Order, Is.EqualTo(0));
			Assert.That(a.Order, Is.EqualTo(1));
			Assert.That(c.Order, Is.EqualTo(2));
			Assert.That(b.ParentId, Is.Null);
		});
	}

	[Test]
	public async Task Move_ReorderSiblingAfter_RenumbersDestinationDensely()
	{
		var a = await AddRoot("A", order: 0, isDefault: true);
		var b = await AddRoot("B", order: 1);
		var c = await AddRoot("C", order: 2);

		var result = await _service.Move(c.Id, a.Id, FolderMovePosition.After);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(a.Order, Is.EqualTo(0));
			Assert.That(c.Order, Is.EqualTo(1));
			Assert.That(b.Order, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Move_Inside_ReparentsAndCompactsTheVacatedSourceGap()
	{
		var root = await AddRoot("Root", order: 0, isDefault: true);
		var target = await AddRoot("Target", order: 1);
		var c1 = await AddFolder("C1", root.Id, order: 0);
		var c2 = await AddFolder("C2", root.Id, order: 1);
		var c3 = await AddFolder("C3", root.Id, order: 2);

		var result = await _service.Move(c2.Id, target.Id, FolderMovePosition.Inside);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(c2.ParentId, Is.EqualTo(target.Id));
			Assert.That(c2.Order, Is.EqualTo(0));
			Assert.That(c1.Order, Is.EqualTo(0));
			Assert.That(c3.Order, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Move_ToRoot_ReparentsAndRenumbersBothGroups()
	{
		var rootA = await AddRoot("RootA", order: 0, isDefault: true);
		var rootB = await AddRoot("RootB", order: 1);
		var nested = await AddFolder("Nested", rootB.Id, order: 0);

		var result = await _service.Move(nested.Id, rootA.Id, FolderMovePosition.After);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(nested.ParentId, Is.Null);
			Assert.That(rootA.Order, Is.EqualTo(0));
			Assert.That(nested.Order, Is.EqualTo(1));
			Assert.That(rootB.Order, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Move_TargetIsSelf_RejectsAndPersistsNothing()
	{
		var a = await AddRoot("A", order: 0, isDefault: true);
		await AddRoot("B", order: 1);
		var savesBefore = _store.SaveCount;

		var result = await _service.Move(a.Id, a.Id, FolderMovePosition.After);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.InvalidParent));
			Assert.That(_store.SaveCount, Is.EqualTo(savesBefore));
		});
	}

	[Test]
	public async Task Move_TargetIsDescendant_RejectsAndPersistsNothing()
	{
		var root = await AddRoot("Root", order: 0, isDefault: true);
		var child = await AddFolder("Child", root.Id, order: 0);
		var savesBefore = _store.SaveCount;

		var result = await _service.Move(root.Id, child.Id, FolderMovePosition.Inside);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.InvalidParent));
			Assert.That(root.ParentId, Is.Null);
			Assert.That(_store.SaveCount, Is.EqualTo(savesBefore));
		});
	}

	[Test]
	public async Task Move_TargetInDifferentProfile_RejectsAndPersistsNothing()
	{
		var folder = await AddRoot("A", order: 0, isDefault: true);

		var otherProfileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = otherProfileId, Name = "Other" });
		var otherProfileTarget = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = otherProfileId,
			Name = "OtherRoot",
			Order = 0,
			Rows = 3,
			Columns = 5,
			IsDefault = true,
			CreatedAt = DateTime.UtcNow
		};
		await new FolderCache(_cache).AddOrUpdate(otherProfileTarget);
		var savesBefore = _store.SaveCount;

		var result = await _service.Move(folder.Id, otherProfileTarget.Id, FolderMovePosition.After);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.InvalidParent));
			Assert.That(_store.SaveCount, Is.EqualTo(savesBefore));
		});
	}

	[Test]
	public async Task Move_MultiSiblingReorder_IncrementsSaveCountByOneForTheWholeBatch()
	{
		var a = await AddRoot("A", order: 0, isDefault: true);
		await AddRoot("B", order: 1);
		var c = await AddRoot("C", order: 2);
		var savesBefore = _store.SaveCount;

		var result = await _service.Move(c.Id, a.Id, FolderMovePosition.After);

		Assert.That(result.Success, Is.True);
		Assert.That(_store.SaveCount, Is.EqualTo(savesBefore + 1));
	}

	[Test]
	public async Task Move_ReplacementStart_IsSelectedAndShippedInTheSameBatch()
	{
		var replacement = await AddRoot("Replacement",
			order: 0,
			createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		var start = await AddRoot("Start",
			order: 1,
			createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		var holder = await AddFolder("Holder", replacement.Id, order: 0);
		await SetStart(start);

		var result = await _service.Move(start.Id, holder.Id, FolderMovePosition.Inside);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(start.IsDefault, Is.False);
			Assert.That(replacement.IsDefault, Is.True);
			Assert.That(result.Data!.Select(f => f.Id), Does.Contain(replacement.Id));
			Assert.That(result.Data!.Select(f => f.Id), Does.Contain(start.Id));
			Assert.That(_store.Get(_profileId)!.Folders.Single(f => f.Id == replacement.Id).IsDefault, Is.True);
		});
	}

	[Test]
	public async Task Move_OnlyRootStartFolder_RejectsMoveInsideAnotherFolder()
	{
		var start = await AddRoot("Start", order: 0, isDefault: true);
		var target = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = _profileId,
			Name = "Orphan",
			ParentId = Guid.NewGuid(),
			Order = 0,
			Rows = 3,
			Columns = 5,
			CreatedAt = DateTime.UtcNow
		};
		await new FolderCache(_cache).AddOrUpdate(target);
		var savesBefore = _store.SaveCount;

		var result = await _service.Move(start.Id, target.Id, FolderMovePosition.Inside);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.InvalidParent));
			Assert.That(start.ParentId, Is.Null);
			Assert.That(start.IsDefault, Is.True);
			Assert.That(_store.SaveCount, Is.EqualTo(savesBefore));
		});
	}

	[Test]
	public async Task Move_StartMarkerFlipWithUnchangedOrder_StillReachesTheNotificationAndDisk()
	{
		var replacement = await AddRoot("R",
			order: 0,
			createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		var middle = await AddRoot("M",
			order: 1,
			createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		var start = await AddRoot("S",
			order: 2,
			createdAt: new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc));
		var holder = await AddFolder("Holder", replacement.Id, order: 0);
		await SetStart(start);
		_mediator.Published.Clear();

		var result = await _service.Move(start.Id, holder.Id, FolderMovePosition.Inside);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(replacement.Order, Is.EqualTo(0));
			Assert.That(middle.Order, Is.EqualTo(1));
			Assert.That(replacement.IsDefault, Is.True);
			Assert.That(start.IsDefault, Is.False);

			var notifications = _mediator.Published.OfType<FoldersReorderedNotification>().ToList();
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].ProfileId, Is.EqualTo(_profileId));
			Assert.That(notifications[0].Folders.Select(f => f.Id),
				Is.EquivalentTo([start.Id, replacement.Id]));

			Assert.That(_store.Get(_profileId)!.Folders.Single(f => f.Id == replacement.Id).IsDefault, Is.True);
		});
	}

	[Test]
	public async Task Move_OrderSurvivesAFreshProfileCacheReinitOverTheSameStore()
	{
		var a = await AddRoot("A", order: 0, isDefault: true);
		var b = await AddRoot("B", order: 1);
		var c = await AddRoot("C", order: 2);

		await _service.Move(c.Id, a.Id, FolderMovePosition.After);
		_cache.Dispose();

		var reloadedCache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await reloadedCache.InitializeCache();

		var reloadedFolders = reloadedCache.GetFoldersByProfileId(_profileId);
		Assert.Multiple(() =>
		{
			Assert.That(reloadedFolders.Select(f => f.Order), Is.EquivalentTo([0, 1, 2]));
			Assert.That(reloadedFolders.Single(f => f.Id == a.Id).Order, Is.EqualTo(0));
			Assert.That(reloadedFolders.Single(f => f.Id == c.Id).Order, Is.EqualTo(1));
			Assert.That(reloadedFolders.Single(f => f.Id == b.Id).Order, Is.EqualTo(2));
		});

		reloadedCache.Dispose();
	}

	[Test]
	public async Task Move_LegacyProfileWithoutCreatedAt_SelectsSameReplacementRootAfterRenumbering()
	{
		var start = await AddFolder("Start", null, order: 5, createdAt: DateTime.MinValue);
		var r1 = await AddFolder("R1", null, order: 3, createdAt: DateTime.MinValue);
		var r2 = await AddFolder("R2", null, order: 1, createdAt: DateTime.MinValue);
		var holder = await AddFolder("Holder", r1.Id, order: 0);
		await SetStart(start);

		var result = await _service.Move(start.Id, holder.Id, FolderMovePosition.Inside);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(r2.IsDefault, Is.True);
			Assert.That(r1.IsDefault, Is.False);
			Assert.That(start.IsDefault, Is.False);
			Assert.That(r2.Order, Is.EqualTo(0));
			Assert.That(r1.Order, Is.EqualTo(1));
			Assert.That(_store.Get(_profileId)!.Folders.Single(f => f.Id == r2.Id).IsDefault, Is.True);
		});
	}

	private async Task<FolderEntity> AddRoot(string name,
		int order,
		DateTime? createdAt = null,
		bool isDefault = false)
		=> await AddFolder(name, null, order, createdAt, isDefault);

	private Task<Domain.Common.Result<FolderEntity, FolderError>> SetStart(FolderEntity folder)
		=> _service.Update(folder.Id, null, null, null, null, null, null, null, null, true);

	private async Task<FolderEntity> AddFolder(string name,
		Guid? parentId,
		int order,
		DateTime? createdAt = null,
		bool isDefault = false,
		Guid? id = null)
	{
		var folder = new FolderEntity
		{
			Id = id ?? Guid.NewGuid(),
			ProfileId = _profileId,
			Name = name,
			ParentId = parentId,
			Order = order,
			Rows = 3,
			Columns = 5,
			IsDefault = isDefault,
			CreatedAt = createdAt ?? DateTime.UtcNow
		};
		await new FolderCache(_cache).AddOrUpdate(folder);
		return folder;
	}
}
