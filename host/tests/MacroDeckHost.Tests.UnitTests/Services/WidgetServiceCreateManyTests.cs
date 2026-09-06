using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetServiceCreateManyTests
{
	private ProfileCache _cache = null!;
	private RecordingMediator _mediator = null!;
	private RecordingWidgetSecretCloner _cloner = null!;
	private WidgetService _service = null!;
	private Guid _profileId;
	private Guid _folderAId;
	private Guid _folderBId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_mediator = new RecordingMediator();
		var secretService = new FakeSecretService();
		_cloner = new RecordingWidgetSecretCloner(new WidgetSecretCloner(secretService));
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			_mediator,
			new WidgetSecretScrubber(secretService),
			_cloner,
			new NullWidgetVariableCloner());

		_profileId = Guid.NewGuid();
		_folderAId = Guid.NewGuid();
		_folderBId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(Folder(_folderAId, "A"));
		await _cache.AddOrUpdateFolder(Folder(_folderBId, "B"));
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task FreeRects_AreCreatedAtExactlyTheRequestedPositions_WithNewIdsNeverPinned()
	{
		var result = await _service.CreateMany(_folderAId, [Widget(0, 0), Widget(1, 0)]);

		Assert.That(result.Success, Is.True);
		var created = result.Data!;
		Assert.Multiple(() =>
		{
			Assert.That(created, Has.Count.EqualTo(2));
			Assert.That(created.Select(w => w.Id).Distinct().Count(), Is.EqualTo(2));
			Assert.That(created, Has.All.Matches<WidgetEntity>(w => w.Id != Guid.Empty));
			Assert.That(created, Has.All.Property(nameof(WidgetEntity.IsPinned)).False);
			Assert.That(created.Select(w => (w.PositionX, w.PositionY)),
				Is.EquivalentTo(new[] { (0, 0), (1, 0) }));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task OneMemberOutOfBounds_FailsWithValidationErrorAndCreatesNothing()
	{
		var result = await _service.CreateMany(_folderAId,
		[
			Widget(0, 0), new WidgetEntity
			{
				Type = WidgetTypeIds.ActionButton,
				PositionX = 3,
				PositionY = 3,
				Width = 2,
				Height = 2
			}
		]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.ValidationError));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets, Is.Empty);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task TwoMembersOverlappingEachOther_FailsWithPositionOccupiedAndCreatesNothing()
	{
		var result = await _service.CreateMany(_folderAId, [Widget(0, 0), Widget(0, 0)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets, Is.Empty);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task OneMemberOverlappingAnExistingWidget_FailsWithPositionOccupiedAndCreatesNothing()
	{
		var existing = Widget(0, 0);
		existing.Id = Guid.NewGuid();
		_cache.AddWidget(_folderAId, existing);
		_mediator.Published.Clear();

		var result = await _service.CreateMany(_folderAId, [Widget(2, 2), Widget(0, 0)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets, Has.Count.EqualTo(1));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task OneMemberOverlappingAForeignPinnedWidget_FailsWithPositionOccupiedAndCreatesNothing()
	{
		var pinned = Widget(2, 2);
		pinned.Id = Guid.NewGuid();
		pinned.IsPinned = true;
		pinned.PinScope = PinScope.Profile;
		_cache.AddWidget(_folderBId, pinned);
		_mediator.Published.Clear();

		var result = await _service.CreateMany(_folderAId, [Widget(0, 0), Widget(2, 2)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets, Is.Empty);
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task ARejectedBatch_ClonesNoSecrets()
	{
		var result = await _service.CreateMany(_folderAId, [Widget(0, 0), Widget(0, 0)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(_cloner.CallCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task AGroupIsPlacedExactlyWhereAsked_AndNeverRelocatedToAFreeSpot()
	{
		var existing = Widget(0, 0);
		existing.Id = Guid.NewGuid();
		_cache.AddWidget(_folderAId, existing);
		_mediator.Published.Clear();

		var result = await _service.CreateMany(_folderAId, [Widget(0, 0)]);

		Assert.That(result.Success, Is.False);
		var widgets = _cache.GetFolderById(_folderAId)!.Widgets;
		Assert.Multiple(() =>
		{
			Assert.That(widgets, Has.Count.EqualTo(1));
			Assert.That((widgets[0].PositionX, widgets[0].PositionY), Is.EqualTo((0, 0)));
		});
	}

	[Test]
	public async Task ACutPasteLandingExactlyOnItsOwnSources_Succeeds_AndEndsWithTheRightWidgetsAtTheRightRects()
	{
		var sourceA = Widget(0, 0);
		sourceA.Id = Guid.NewGuid();
		var sourceB = Widget(1, 0);
		sourceB.Id = Guid.NewGuid();
		_cache.AddWidget(_folderAId, sourceA);
		_cache.AddWidget(_folderAId, sourceB);
		_mediator.Published.Clear();

		// The pasted group anchors exactly on top of the two widgets it replaces - the scenario the
		// old whole-set overlap check rejected every time (issue #213).
		var result = await _service.CreateMany(_folderAId,
			[Widget(0, 0), Widget(1, 0)],
			[sourceA.Id, sourceB.Id]);

		Assert.That(result.Success, Is.True);
		var widgets = _cache.GetFolderById(_folderAId)!.Widgets;
		Assert.Multiple(() =>
		{
			Assert.That(widgets, Has.Count.EqualTo(2));
			Assert.That(widgets.Select(w => w.Id), Does.Not.Contain(sourceA.Id));
			Assert.That(widgets.Select(w => w.Id), Does.Not.Contain(sourceB.Id));
			Assert.That(widgets.Select(w => (w.PositionX, w.PositionY)),
				Is.EquivalentTo(new[] { (0, 0), (1, 0) }));
			Assert.That(_mediator.Published.OfType<WidgetsDeletedNotification>().Single().WidgetIds,
				Is.EquivalentTo(new[] { sourceA.Id, sourceB.Id }));
			Assert.That(_mediator.Published.OfType<WidgetsCreatedNotification>().Single().Widgets,
				Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task AReplaceIdNotInTheFolder_FailsWithNotFound_AndMutatesNothing()
	{
		var source = Widget(0, 0);
		_cache.AddWidget(_folderAId, source);
		_mediator.Published.Clear();

		var result = await _service.CreateMany(_folderAId, [Widget(0, 0)], [Guid.NewGuid()]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.NotFound));
			var widgets = _cache.GetFolderById(_folderAId)!.Widgets;
			Assert.That(widgets, Has.Count.EqualTo(1));
			Assert.That(widgets[0].Id, Is.EqualTo(source.Id));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task AReplaceIdInAnotherFolder_FailsWithNotFound_AndMutatesNothing()
	{
		var inB = Widget(0, 0);
		_cache.AddWidget(_folderBId, inB);
		_mediator.Published.Clear();

		var result = await _service.CreateMany(_folderAId, [Widget(0, 0)], [inB.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.NotFound));
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets, Is.Empty);
			Assert.That(_cache.GetFolderById(_folderBId)!.Widgets, Has.Count.EqualTo(1));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task ARejectedReplacePaste_ClonesNoSecrets_AndDeletesNothing()
	{
		var source = Widget(0, 0);
		_cache.AddWidget(_folderAId, source);
		_mediator.Published.Clear();

		var result = await _service.CreateMany(_folderAId, [Widget(2, 2), Widget(2, 2)], [source.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(_cloner.CallCount, Is.EqualTo(0));
			var widgets = _cache.GetFolderById(_folderAId)!.Widgets;
			Assert.That(widgets, Has.Count.EqualTo(1));
			Assert.That(widgets[0].Id, Is.EqualTo(source.Id));
			Assert.That(_mediator.Published, Is.Empty);
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
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};

	private sealed class RecordingWidgetSecretCloner : IWidgetSecretCloner
	{
		private readonly IWidgetSecretCloner _inner;

		public RecordingWidgetSecretCloner(IWidgetSecretCloner inner) => _inner = inner;

		public int CallCount { get; private set; }

		public Task<string?> CloneReferencedSecrets(string? widgetData)
		{
			CallCount++;
			return _inner.CloneReferencedSecrets(widgetData);
		}
	}
}
