using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderStartFolderTests
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
	public async Task Update_SetAsStartFolder_ClearsTheOtherRootAndPersistsBothMarkersTogether()
	{
		var first = await AddRoot("First", createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		var second = await AddRoot("Second", createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));

		var result = await Update(second.Id, isDefault: true);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(first.IsDefault, Is.False);
			Assert.That(second.IsDefault, Is.True);
			Assert.That(_store.Get(_profileId)!.Folders.Single(folder => folder.Id == first.Id).IsDefault, Is.False);
			Assert.That(_store.Get(_profileId)!.Folders.Single(folder => folder.Id == second.Id).IsDefault, Is.True);
		});
	}

	[Test]
	public async Task Update_RejectsChildAsStartAndDoesNotMutateThePersistedMarker()
	{
		var root = await AddRoot("Home");
		var child = await AddFolder("Child", root.Id);

		var result = await Update(child.Id, isDefault: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.ValidationError));
			Assert.That(root.IsDefault, Is.True);
			Assert.That(child.IsDefault, Is.False);
			Assert.That(_store.Get(_profileId)!.Folders.Single(folder => folder.Id == root.Id).IsDefault, Is.True);
		});
	}

	[Test]
	public async Task Update_RejectsDisablingTheOnlyStartMarker()
	{
		var root = await AddRoot("Home");

		var result = await Update(root.Id, isDefault: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.ValidationError));
			Assert.That(root.IsDefault, Is.True);
		});
	}

	[Test]
	public async Task Delete_CurrentStart_SelectsTheOldestRemainingRoot()
	{
		var current = await AddRoot("Current", createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		var replacement = await AddRoot("Oldest", createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		await Update(current.Id, isDefault: true);
		_mediator.Published.Clear();

		var result = await _service.Delete(current.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(replacement.IsDefault, Is.True);
			Assert.That(_store.Get(_profileId)!.Folders.Single().Id, Is.EqualTo(replacement.Id));
			Assert.That(_store.Get(_profileId)!.Folders.Single().IsDefault, Is.True);
			Assert.That(_mediator.Published.OfType<FolderUpdatedNotification>()
					.Select(notification => notification.Folder.Id),
				Is.EquivalentTo([replacement.Id]));
			Assert.That(_mediator.Published.Last(), Is.TypeOf<FolderDeletedNotification>());
		});
	}

	[Test]
	public async Task Update_MovingCurrentStartInsideAnotherFolder_SelectsTheOldestRemainingRoot()
	{
		var replacement = await AddRoot("Oldest", createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		var current = await AddRoot("Current", createdAt: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
		await Update(current.Id, isDefault: true);

		var result = await Update(current.Id, parentId: replacement.Id);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(current.ParentId, Is.EqualTo(replacement.Id));
			Assert.That(current.IsDefault, Is.False);
			Assert.That(replacement.IsDefault, Is.True);
			Assert.That(_store.Get(_profileId)!.Folders.Single(folder => folder.Id == replacement.Id).IsDefault,
				Is.True);
		});
	}

	[Test]
	public async Task Update_InvalidCompoundMove_DoesNotLeaveTheStartFolderMovedInMemoryOrOnDisk()
	{
		var current = await AddRoot("Current");
		var target = await AddRoot("Target");

		var result = await Update(current.Id, parentId: target.Id, rows: 0);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(current.ParentId, Is.Null);
			Assert.That(current.IsDefault, Is.True);
			Assert.That(target.IsDefault, Is.False);
			Assert.That(_store.Get(_profileId)!.Folders.Single(folder => folder.Id == current.Id).ParentId, Is.Null);
			Assert.That(_store.Get(_profileId)!.Folders.Single(folder => folder.Id == current.Id).IsDefault, Is.True);
		});
	}

	private async Task<FolderEntity> AddRoot(string name, DateTime? createdAt = null)
		=> await AddFolder(name, parentId: null, createdAt);

	private async Task<FolderEntity> AddFolder(string name, Guid? parentId, DateTime? createdAt = null)
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
			CreatedAt = createdAt ?? DateTime.UtcNow
		};
		await new FolderCache(_cache).AddOrUpdate(folder);
		return folder;
	}

	private Task<Domain.Common.Result<FolderEntity, FolderError>> Update(Guid id,
		Guid? parentId = null,
		bool? isDefault = null,
		int? rows = null,
		int? order = null)
		=> _service.Update(id, null, parentId, order, rows, null, null, null, null, isDefault);

	[Test]
	public async Task Update_SparseSiblingOrders_ClampsTheRequestedIndexAndRenumbersDensely()
	{
		var parent = await AddRoot("Parent");
		var low = await AddFolder("Low", parent.Id);
		low.Order = 0;
		var mid = await AddFolder("Mid", parent.Id);
		mid.Order = 10;
		var high = await AddFolder("High", parent.Id);
		high.Order = 20;

		var result = await Update(low.Id, order: 999);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(mid.Order, Is.EqualTo(0));
			Assert.That(high.Order, Is.EqualTo(1));
			Assert.That(low.Order, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Update_Reparent_RenumbersDestinationAndSourceListsInOneWrite()
	{
		var parentOne = await AddRoot("ParentOne");
		var a = await AddFolder("A", parentOne.Id);
		var b = await AddFolder("B", parentOne.Id);
		var c = await AddFolder("C", parentOne.Id);
		var parentTwo = await AddRoot("ParentTwo");
		var d = await AddFolder("D", parentTwo.Id);
		var e = await AddFolder("E", parentTwo.Id);
		var savesBefore = _store.SaveCount;

		var result = await Update(b.Id, parentId: parentTwo.Id);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(b.ParentId, Is.EqualTo(parentTwo.Id));
			Assert.That(d.Order, Is.EqualTo(0));
			Assert.That(e.Order, Is.EqualTo(1));
			Assert.That(b.Order, Is.EqualTo(2));
			Assert.That(a.Order, Is.EqualTo(0));
			Assert.That(c.Order, Is.EqualTo(1));
			Assert.That(_store.SaveCount, Is.EqualTo(savesBefore + 1));
		});
	}
}
