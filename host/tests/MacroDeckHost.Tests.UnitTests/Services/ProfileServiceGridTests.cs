using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class ProfileServiceGridTests
{
	private ProfileCache _cache = null!;
	private FolderService _folderService = null!;
	private ProfileService _profileService = null!;
	private WidgetService _widgetService = null!;
	private Guid _profileId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secrets = new FakeSecretService();
		var folderCache = new FolderCache(_cache);
		_folderService = new FolderService(folderCache,
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
		_profileService
			= new ProfileService(_cache, folderCache, new InMemoryDeviceRepository(), new RecordingMediator());
		_widgetService = new WidgetService(folderCache,
			_cache,
			new RecordingMediator(),
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner());

		var created = await _profileService.Create("Gaming", defaultRows: 3, defaultColumns: 5);
		_profileId = created.Data!.Id;
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Update_ShrinkingDefaultRowsBelowAnInheritingFoldersWidgets_FailsAndNamesTheFolder_AppliesNothing()
	{
		var folder = await CreateFolder("Games"); // fully inherited grid (3 x 5)
		_cache.AddWidget(folder.Id, Widget(x: 0, y: 2)); // needs 3 rows - fits exactly at the default

		var result = await _profileService.Update(_profileId, "Renamed", null, 2, null, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.GridTooSmall));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
			Assert.That(_cache.GetById(_profileId)!.Name, Is.EqualTo("Gaming"));
			Assert.That(_cache.GetById(_profileId)!.DefaultRows, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task Update_ShrinkingDefaultRows_AllowedWhenEveryFolderPinsItsOwnGrid()
	{
		var home = _cache.GetFoldersByProfileId(_profileId).Single();
		await _folderService.Update(home.Id, null, null, null, 3, 5, null, null, null, null);
		var games = await CreateFolder("Games");
		await _folderService.Update(games.Id, null, null, null, 5, null, null, null, null, null);
		_cache.AddWidget(games.Id, Widget(x: 0, y: 2));

		var result = await _profileService.Update(_profileId, null, null, 2, null, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task Update_GrowingTheDefaults_IsAlwaysAllowed()
	{
		var folder = await CreateFolder("Games");

		var result = await _profileService.Update(_profileId, null, null, 10, 10, null, null, null);

		Assert.That(result.Success, Is.True);
		Assert.That(GridInheritance.ResolveRows(_cache.GetFolderById(folder.Id)!,
				_cache.GetFoldersByProfileId(_profileId),
				10),
			Is.EqualTo(10));
	}

	[Test]
	public async Task Update_AnInheritingFoldersEffectiveGridFollowsTheProfileEdit()
	{
		// Issue #32: editing the grid size through Edit Profile used to do nothing, because folder
		// creation copied the profile defaults instead of inheriting them. A folder created inherited
		// must pick up a later profile default edit immediately.
		var folder = _cache.GetFoldersByProfileId(_profileId).Single();
		Assert.That(folder.Rows, Is.Null); // sanity: the seeded start folder is created inherited

		await _profileService.Update(_profileId, null, null, 7, 9, null, null, null);

		var updatedProfile = _cache.GetById(_profileId)!;
		var profileFolders = _cache.GetFoldersByProfileId(_profileId);
		var updatedFolder = _cache.GetFolderById(folder.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(updatedProfile.DefaultRows, Is.EqualTo(7));
			Assert.That(updatedProfile.DefaultColumns, Is.EqualTo(9));
			Assert.That(updatedFolder.Rows, Is.Null); // still inherited, not rewritten by the profile edit
			Assert.That(GridInheritance.ResolveRows(updatedFolder, profileFolders, updatedProfile.DefaultRows),
				Is.EqualTo(7));
			Assert.That(GridInheritance.ResolveColumns(updatedFolder, profileFolders, updatedProfile.DefaultColumns),
				Is.EqualTo(9));
		});
	}

	[Test]
	public async Task Update_SetsDefaultWidgetSpacingAndBorderRadius()
	{
		var result = await _profileService.Update(_profileId, null, null, null, null, null, 20, 30);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.DefaultWidgetSpacing, Is.EqualTo(20));
			Assert.That(result.Data!.DefaultWidgetBorderRadius, Is.EqualTo(30));
		});
	}

	[Test]
	public async Task Update_MinusOneClearsDefaultWidgetSpacingAndBorderRadius()
	{
		await _profileService.Update(_profileId, null, null, null, null, null, 20, 30);

		var result = await _profileService.Update(_profileId, null, null, null, null, null, -1, -1);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.DefaultWidgetSpacing, Is.Null);
			Assert.That(result.Data!.DefaultWidgetBorderRadius, Is.Null);
		});
	}

	[Test]
	public async Task Update_NullLeavesDefaultWidgetSpacingAndBorderRadiusUnchanged()
	{
		await _profileService.Update(_profileId, null, null, null, null, null, 20, 30);

		var result = await _profileService.Update(_profileId, null, null, null, null, null, null, null);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.DefaultWidgetSpacing, Is.EqualTo(20));
			Assert.That(result.Data!.DefaultWidgetBorderRadius, Is.EqualTo(30));
		});
	}

	[TestCase(-2)]
	[TestCase(41)]
	public async Task Update_RejectsOutOfRangeDefaultWidgetSpacing(int value)
	{
		var result = await _profileService.Update(_profileId, null, null, null, null, null, value, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.ValidationError));
		});
	}

	[TestCase(-2)]
	[TestCase(61)]
	public async Task Update_RejectsOutOfRangeDefaultWidgetBorderRadius(int value)
	{
		var result = await _profileService.Update(_profileId, null, null, null, null, null, null, value);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(ProfileError.ValidationError));
		});
	}

	[Test]
	public async Task
		Update_DefaultGridReduction_IsScopeAware_SucceedsOutOfReach_RejectedWhenThePinsOwnSubtreeCannotFit()
	{
		// Issue #245: ProfileService.Update takes no code change for this - it becomes scope-aware
		// through PinnedWidgetLayout.Required*, which GridInheritance.FindGridConflicts calls per folder.
		var main = _cache.GetFoldersByProfileId(_profileId).Single(); // seeded start folder, fully inherited
		var games = await CreateFolder("Games", main.Id); // child of Main - inside the pin's subtree
		var away = await CreateFolder("Away"); // a separate root - outside the pin's subtree
		_cache.AddWidget(away.Id, Widget(x: 0, y: 0)); // needs 1 row - unrelated to the pin

		var z = Widget(x: 0, y: 2); // needs 3 rows - exactly today's default
		_cache.AddWidget(main.Id, z);
		var pinned = await _widgetService.SetPinned(main.Id, z.Id, true, PinScope.Subtree);
		Assert.That(pinned.Success, Is.True);

		var grown = await _profileService.Update(_profileId, null, null, 5, null, null, null, null);
		Assert.That(grown.Success, Is.True);
		var succeeded = await _profileService.Update(_profileId, null, null, 3, null, null, null, null);
		Assert.Multiple(() =>
		{
			Assert.That(succeeded.Success, Is.True);
			Assert.That(_cache.GetById(_profileId)!.DefaultRows, Is.EqualTo(3));
		});

		var rejected = await _profileService.Update(_profileId, null, null, 2, null, null, null, null);
		Assert.Multiple(() =>
		{
			Assert.That(rejected.Success, Is.False);
			Assert.That(rejected.Error, Is.EqualTo(ProfileError.GridTooSmall));
			Assert.That(rejected.ErrorMessage, Does.Contain(main.Name).Or.Contain(games.Name));
			Assert.That(_cache.GetById(_profileId)!.DefaultRows, Is.EqualTo(3));
		});
	}

	private async Task<FolderEntity> CreateFolder(string name, Guid? parentId = null)
		=> (await _folderService.Create(_profileId, name, parentId)).Data!;

	private static WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};
}
