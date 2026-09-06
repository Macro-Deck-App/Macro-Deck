using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceGridInheritanceTests
{
	private ProfileCache _cache = null!;
	private FolderService _service = null!;
	private Guid _profileId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secrets = new FakeSecretService();
		_service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());

		_profileId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity
			{ Id = _profileId, Name = "P", DefaultRows = 3, DefaultColumns = 5 });
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Create_YieldsAFullyInheritedGrid()
	{
		var result = await _service.Create(_profileId, "Home", null);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Rows, Is.Null);
			Assert.That(result.Data!.Columns, Is.Null);
		});
	}

	[Test]
	public async Task Create_PinsOnlyTheDeficientAxisForAPinnedWidget()
	{
		var home = await CreateFolder("Home");
		_cache.AddWidget(home.Id, Widget(x: 6, y: 1, pinned: true));

		var result = await _service.Create(_profileId, "New", null);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Columns, Is.EqualTo(7));
			Assert.That(result.Data!.Rows, Is.Null);
		});
	}

	[Test]
	public async Task Update_MinusOneClearsTheGrid_AndTheFolderFollowsItsParentThenTheProfile()
	{
		var root = await CreateFolder("Root");
		await _service.Update(root.Id, null, null, null, 6, 8, null, null, null, null);
		var child = await CreateFolder("Child", root.Id);
		await _service.Update(child.Id, null, null, null, 4, 4, null, null, null, null);

		var cleared = await _service.Update(child.Id, null, null, null, -1, -1, null, null, null, null);

		Assert.That(cleared.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(cleared.Data!.Rows, Is.Null);
			Assert.That(cleared.Data!.Columns, Is.Null);
			Assert.That(GridInheritance.ResolveRows(cleared.Data!, _cache.GetFoldersByProfileId(_profileId), 3),
				Is.EqualTo(6)); // follows the parent
		});

		await _service.Update(root.Id, null, null, null, -1, -1, null, null, null, null);

		Assert.That(GridInheritance.ResolveRows(_cache.GetFolderById(child.Id)!,
				_cache.GetFoldersByProfileId(_profileId),
				3),
			Is.EqualTo(3)); // the parent inherited too, so the child now follows the profile default
	}

	[Test]
	public async Task Update_MinusOneIsRejected_WhenTheInheritedGridIsSmallerThanTheFoldersOwnWidgets()
	{
		var folder = await CreateFolder("Games");
		await _service.Update(folder.Id, null, null, null, 6, 6, null, null, null, null);
		_cache.AddWidget(folder.Id, Widget(x: 4, y: 4)); // needs 5 x 5; the profile default is only 5 x 3

		var result = await _service.Update(folder.Id, null, null, null, -1, null, null, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
			Assert.That(_cache.GetFolderById(folder.Id)!.Rows, Is.EqualTo(6));
		});
	}

	[Test]
	public async Task Update_ShrinkingAParentBelowAnInheritingChild_FailsAndNamesTheChild_RawValuesUntouched()
	{
		var parent = await CreateFolder("Parent");
		await _service.Update(parent.Id, null, null, null, 6, 6, null, null, null, null);
		var child = await CreateFolder("Games", parent.Id); // inherits 6 x 6 from the parent
		_cache.AddWidget(child.Id, Widget(x: 4, y: 4)); // needs 5 x 5 - fits the inherited 6 x 6

		var result = await _service.Update(parent.Id, null, null, null, 2, 2, null, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
		});
		var storedParent = _cache.GetFolderById(parent.Id)!;
		var storedChild = _cache.GetFolderById(child.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That((storedParent.Rows, storedParent.Columns), Is.EqualTo(((int?)6, (int?)6)));
			Assert.That(storedChild.Rows, Is.Null);
			Assert.That(storedChild.Columns, Is.Null);
		});
	}

	[Test]
	public async Task Update_ACompoundUpdateThatFailsTheGridCheck_IsRejectedAtomically()
	{
		var parent = await CreateFolder("Parent");
		await _service.Update(parent.Id, null, null, null, 6, 6, null, null, null, null);
		var child = await CreateFolder("Games", parent.Id);
		_cache.AddWidget(child.Id, Widget(x: 4, y: 4));

		var result = await _service.Update(parent.Id, "Renamed", null, null, 2, 2, "#fff", null, null, null);

		Assert.That(result.Success, Is.False);
		var stored = _cache.GetFolderById(parent.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(stored.Name, Is.EqualTo("Parent"));
			Assert.That(stored.BackgroundColor, Is.Null);
		});
	}

	[Test]
	public async Task Update_APreExistingOutOfGridFolder_DoesNotBlockAnUnrelatedGridEdit()
	{
		var stuck = await CreateFolder("Stuck");
		await _service.Update(stuck.Id, null, null, null, 2, 2, null, null, null, null);
		_cache.AddWidget(stuck.Id, Widget(x: 5, y: 5));
		var unrelated = await CreateFolder("Unrelated");

		var result = await _service.Update(unrelated.Id, null, null, null, 4, 4, null, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task Move_OutOfABigParent_IsRejectedWhenTheInheritedGridWouldShrinkBelowTheWidgets()
	{
		var big = await CreateFolder("Big");
		await _service.Update(big.Id, null, null, null, 8, 8, null, null, null, null);
		var games = await CreateFolder("Games", big.Id); // inherits 8 x 8
		_cache.AddWidget(games.Id, Widget(x: 0, y: 6)); // needs 7 rows; the profile default is only 3

		var result = await _service.Move(games.Id, big.Id, FolderMovePosition.After);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
			Assert.That(_cache.GetFolderById(games.Id)!.ParentId, Is.EqualTo(big.Id));
		});
	}

	[Test]
	public async Task Move_ThatKeepsEveryWidgetInsideTheResolvedGrid_IsAllowed()
	{
		var big = await CreateFolder("Big");
		await _service.Update(big.Id, null, null, null, 8, 8, null, null, null, null);
		var games = await CreateFolder("Games", big.Id);
		_cache.AddWidget(games.Id, Widget(x: 0, y: 1)); // fits the 3-row profile default too

		var result = await _service.Move(games.Id, big.Id, FolderMovePosition.After);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(games.Id)!.ParentId, Is.Null);
	}

	[Test]
	public async Task Update_AReparentAloneIsRejected_WhenTheInheritedGridWouldShrinkBelowTheWidgets()
	{
		var big = await CreateFolder("Big");
		await _service.Update(big.Id, null, null, null, 8, 8, null, null, null, null);
		var games = await CreateFolder("Games", big.Id);
		_cache.AddWidget(games.Id, Widget(x: 0, y: 6));

		var result = await _service.Update(games.Id, null, Guid.Empty, null, null, null, null, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
			Assert.That(_cache.GetFolderById(games.Id)!.ParentId, Is.EqualTo(big.Id));
		});
	}

	[Test]
	public async Task Update_AReparentCombinedWithAResetGrid_IsCheckedAgainstTheNewParent()
	{
		var big = await CreateFolder("Big");
		await _service.Update(big.Id, null, null, null, 8, 8, null, null, null, null);
		var games = await CreateFolder("Games", big.Id);
		await _service.Update(games.Id, null, null, null, 8, 8, null, null, null, null); // explicit 8 x 8
		_cache.AddWidget(games.Id, Widget(x: 0, y: 6));

		var result = await _service.Update(games.Id, null, Guid.Empty, null, -1, -1, null, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
			Assert.That(_cache.GetFolderById(games.Id)!.Rows, Is.EqualTo(8));
		});
	}

	private async Task<FolderEntity> CreateFolder(string name, Guid? parentId = null)
		=> (await _service.Create(_profileId, name, parentId)).Data!;

	private static WidgetEntity Widget(int x, int y, bool pinned = false) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1,
		IsPinned = pinned
	};
}
