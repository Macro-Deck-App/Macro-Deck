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
public class WidgetPinningTests
{
	private ProfileCache _cache = null!;
	private WidgetService _widgetService = null!;
	private FolderService _folderService = null!;
	private Guid _profileId;
	private Guid _folderAId;
	private Guid _folderBId;
	private WidgetEntity _widget = null!;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var secretService = new FakeSecretService();
		var folderCache = new FolderCache(_cache);
		_widgetService = new WidgetService(folderCache,
			_cache,
			new RecordingMediator(),
			new WidgetSecretScrubber(secretService),
			new WidgetSecretCloner(secretService),
			new NullWidgetVariableCloner());
		_folderService = new FolderService(folderCache,
			_cache,
			new InMemoryDeviceRepository(),
			new RecordingMediator(),
			new WidgetSecretCloner(secretService),
			new WidgetSecretScrubber(secretService),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());

		_profileId = Guid.NewGuid();
		_folderAId = Guid.NewGuid();
		_folderBId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity
		{
			Id = _profileId,
			Name = "P",
			DefaultRows = 4,
			DefaultColumns = 4
		});
		await _cache.AddOrUpdateFolder(Folder(_folderAId, "Main"));
		await _cache.AddOrUpdateFolder(Folder(_folderBId, "Games"));

		_widget = Widget(0, 0);
		_cache.AddWidget(_folderAId, _widget);
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task SetPinned_FreeInEveryFolder_Pins()
	{
		var result = await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single().IsPinned, Is.True);
	}

	[Test]
	public async Task SetPinned_CellOccupiedInAnotherFolder_FailsAndNamesThatFolder()
	{
		_cache.AddWidget(_folderBId, Widget(0, 0));

		var result = await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
		});
		Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single().IsPinned, Is.False);
	}

	[Test]
	public async Task SetPinned_OverlappedByAMultiCellWidgetElsewhere_Fails()
	{
		var wide = Widget(0, 0);
		wide.Width = 3;
		wide.Height = 2;
		_cache.AddWidget(_folderAId, wide);
		var target = Widget(2, 1);
		_cache.AddWidget(_folderBId, target);

		var result = await _widgetService.SetPinned(_folderBId, target.Id, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task SetPinned_RectOutsideASmallerFolderGrid_Fails()
	{
		var small = _cache.GetFolderById(_folderBId)!;
		small.Columns = 2;
		small.Rows = 2;
		await _cache.AddOrUpdateFolder(small);

		var far = Widget(3, 3);
		_cache.AddWidget(_folderAId, far);

		var result = await _widgetService.SetPinned(_folderAId, far.Id, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task Unpin_IsAlwaysAllowed()
	{
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		var result = await _widgetService.SetPinned(_folderAId, _widget.Id, false);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single().IsPinned, Is.False);
	}

	[Test]
	public async Task UpdatePositions_RejectsMovingAPinnedWidget()
	{
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		var result = await _widgetService.UpdatePositions(_folderAId,
			[new WidgetPlacement(_widget.Id, 2, 2, 1, 1)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.WidgetPinned));
		});
		var stored = _cache.GetFolderById(_folderAId)!.Widgets.Single();
		Assert.That((stored.PositionX, stored.PositionY), Is.EqualTo((0, 0)));
	}

	[Test]
	public async Task UpdatePositions_RejectsMovingAnotherWidgetOntoAPinnedOne()
	{
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);
		var other = Widget(1, 1);
		_cache.AddWidget(_folderBId, other);

		var result = await _widgetService.UpdatePositions(_folderBId,
			[new WidgetPlacement(other.Id, 0, 0, 1, 1)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task Update_RejectsAChangedRectOnAPinnedWidgetButKeepsTheFlag()
	{
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		var moved = new WidgetEntity
		{
			Id = _widget.Id,
			FolderId = _folderAId,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 2,
			PositionY = 2,
			Width = 1,
			Height = 1
		};
		var result = await _widgetService.Update(moved);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.WidgetPinned));
		});
		Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single().IsPinned, Is.True);
	}

	[Test]
	public async Task Update_KeepsThePinFlagTheUpdateDtoDoesNotCarry()
	{
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		var edited = new WidgetEntity
		{
			Id = _widget.Id,
			FolderId = _folderAId,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			Data = "{\"label\":\"edited\"}"
		};
		var result = await _widgetService.Update(edited);

		Assert.That(result.Success, Is.True);
		Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single().IsPinned, Is.True);
	}

	[Test]
	public async Task Create_RefusesACellReservedByAPinnedWidget()
	{
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		var result = await _widgetService.Create(_folderBId, Widget(0, 0));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task FolderUpdate_RefusesShrinkingTheGridBelowAPinnedWidget()
	{
		var far = Widget(3, 3);
		_cache.AddWidget(_folderAId, far);
		await _widgetService.SetPinned(_folderAId, far.Id, true);

		var result = await _folderService.Update(_folderBId,
			null,
			null,
			null,
			2,
			2,
			null,
			null,
			null,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
		});
		var folder = _cache.GetFolderById(_folderBId)!;
		Assert.That((folder.Columns, folder.Rows), Is.EqualTo(((int?)4, (int?)4)));
	}

	[Test]
	public async Task FolderUpdate_RefusesShrinkingBelowTheFoldersOwnWidgets()
	{
		_cache.AddWidget(_folderAId, Widget(3, 0));

		var result = await _folderService.Update(_folderAId,
			null,
			null,
			null,
			null,
			2,
			null,
			null,
			null,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.GridTooSmall));
		});
	}

	[Test]
	public async Task FolderCreate_GrowsTheGridSoAPinnedWidgetFits()
	{
		var small = _cache.GetById(_profileId)!;
		small.DefaultColumns = 2;
		small.DefaultRows = 2;
		await _cache.AddOrUpdate(small);

		var far = Widget(3, 3);
		_cache.AddWidget(_folderAId, far);
		await _widgetService.SetPinned(_folderAId, far.Id, true);

		var result = await _folderService.Create(_profileId, "New", null);

		Assert.That(result.Success, Is.True);
		Assert.That((result.Data!.Columns, result.Data!.Rows), Is.EqualTo(((int?)4, (int?)4)));
	}

	[Test]
	public async Task FolderDuplicate_SkipsPinnedWidgetsSoTheCopyDoesNotDoubleThem()
	{
		var normal = Widget(1, 1);
		_cache.AddWidget(_folderAId, normal);
		await _widgetService.SetPinned(_folderAId, _widget.Id, true);

		var result = await _folderService.Duplicate(_folderAId);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Widgets, Has.Count.EqualTo(1));
			Assert.That(result.Data!.Widgets.Single().PositionX, Is.EqualTo(1));
		});
	}

	private FolderEntity Folder(Guid id, string name) => new()
	{
		Id = id,
		ProfileId = _profileId,
		Name = name,
		Order = 0,
		Rows = 4,
		Columns = 4
	};

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
