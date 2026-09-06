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
public class WidgetServiceSetPinnedManyTests
{
	private ProfileCache _cache = null!;
	private RecordingMediator _mediator = null!;
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
		_service = new WidgetService(new FolderCache(_cache),
			_cache,
			_mediator,
			new WidgetSecretScrubber(secretService),
			new WidgetSecretCloner(secretService),
			new NullWidgetVariableCloner());

		_profileId = Guid.NewGuid();
		_folderAId = Guid.NewGuid();
		_folderBId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity
			{ Id = _profileId, Name = "P", DefaultRows = 4, DefaultColumns = 4 });
		await _cache.AddOrUpdateFolder(Folder(_folderAId, "Main"));
		await _cache.AddOrUpdateFolder(Folder(_folderBId, "Games"));
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task PinningSeveralWidgetsThatFitInEveryReachedFolder_PinsAllOfThem()
	{
		var w0 = Widget(0, 0);
		var w1 = Widget(1, 0);
		_cache.AddWidget(_folderAId, w0);
		_cache.AddWidget(_folderAId, w1);

		var result = await _service.SetPinnedMany(_folderAId, [w0.Id, w1.Id], true);

		Assert.That(result.Success, Is.True);
		var folder = _cache.GetFolderById(_folderAId)!;
		Assert.Multiple(() =>
		{
			Assert.That(folder.Widgets.Single(w => w.Id == w0.Id).IsPinned, Is.True);
			Assert.That(folder.Widgets.Single(w => w.Id == w1.Id).IsPinned, Is.True);
		});
	}

	[Test]
	public async Task OneMemberConflictingInOneReachedFolder_PinsNothing_AndNamesThatFolder()
	{
		var free = Widget(0, 0);
		var conflicting = Widget(1, 0);
		_cache.AddWidget(_folderAId, free);
		_cache.AddWidget(_folderAId, conflicting);
		// Occupies (1,0) in Games, so pinning `conflicting` with the default Profile scope cannot fit there.
		_cache.AddWidget(_folderBId, Widget(1, 0));

		var result = await _service.SetPinnedMany(_folderAId, [free.Id, conflicting.Id], true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
			// The decisive assertion against a per-widget loop: even the member that would have fit on its
			// own is left unpinned because the whole batch failed.
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single(w => w.Id == free.Id).IsPinned, Is.False);
			Assert.That(_cache.GetFolderById(_folderAId)!.Widgets.Single(w => w.Id == conflicting.Id).IsPinned,
				Is.False);
		});
	}

	[Test]
	public async Task ASubtreeScopedBatch_IsValidatedOnlyAgainstTheSubtree()
	{
		var fixture = new PinScopeFixture();
		try
		{
			var w0 = fixture.AddWidget(fixture.Main, 0, 0);
			var w1 = fixture.AddWidget(fixture.Main, 1, 0);
			fixture.AddWidget(fixture.Work, 0, 0);
			fixture.AddWidget(fixture.Work, 1, 0);

			var result = await fixture.WidgetService.SetPinnedMany(fixture.Main.Id,
				[w0.Id, w1.Id],
				true,
				PinScope.Subtree);

			Assert.That(result.Success, Is.True);
			var main = fixture.Reload(fixture.Main);
			Assert.Multiple(() =>
			{
				Assert.That(main.Widgets.Single(w => w.Id == w0.Id).IsPinned, Is.True);
				Assert.That(main.Widgets.Single(w => w.Id == w1.Id).IsPinned, Is.True);
			});
		}
		finally
		{
			fixture.Dispose();
		}
	}

	[Test]
	public async Task PinningAMixedSelection_EndsWithBothPinned()
	{
		var alreadyPinned = Widget(0, 0);
		var notPinned = Widget(1, 0);
		_cache.AddWidget(_folderAId, alreadyPinned);
		_cache.AddWidget(_folderAId, notPinned);
		await _service.SetPinned(_folderAId, alreadyPinned.Id, true);

		var result = await _service.SetPinnedMany(_folderAId, [alreadyPinned.Id, notPinned.Id], true);

		Assert.That(result.Success, Is.True);
		var folder = _cache.GetFolderById(_folderAId)!;
		Assert.Multiple(() =>
		{
			Assert.That(folder.Widgets.Single(w => w.Id == alreadyPinned.Id).IsPinned, Is.True);
			Assert.That(folder.Widgets.Single(w => w.Id == notPinned.Id).IsPinned, Is.True);
		});
	}

	[Test]
	public async Task UnpinningAMixedSelection_EndsWithBothUnpinned()
	{
		var stillPinned = Widget(0, 0);
		var alreadyUnpinned = Widget(1, 0);
		_cache.AddWidget(_folderAId, stillPinned);
		_cache.AddWidget(_folderAId, alreadyUnpinned);
		await _service.SetPinned(_folderAId, stillPinned.Id, true);

		var result = await _service.SetPinnedMany(_folderAId, [stillPinned.Id, alreadyUnpinned.Id], false);

		Assert.That(result.Success, Is.True);
		var folder = _cache.GetFolderById(_folderAId)!;
		Assert.Multiple(() =>
		{
			Assert.That(folder.Widgets.Single(w => w.Id == stillPinned.Id).IsPinned, Is.False);
			Assert.That(folder.Widgets.Single(w => w.Id == alreadyUnpinned.Id).IsPinned, Is.False);
		});
	}

	[Test]
	public async Task Unpin_NeverFailsOnLayout_AndResetsEveryMembersScopeToProfile()
	{
		var w0 = Widget(0, 0);
		var w1 = Widget(1, 0);
		_cache.AddWidget(_folderAId, w0);
		_cache.AddWidget(_folderAId, w1);
		await _service.SetPinned(_folderAId, w0.Id, true, PinScope.Subtree);
		await _service.SetPinned(_folderAId, w1.Id, true, PinScope.Subtree);
		_cache.AddWidget(_folderBId, Widget(0, 0));
		_cache.AddWidget(_folderBId, Widget(1, 0));

		var result = await _service.SetPinnedMany(_folderAId, [w0.Id, w1.Id], false);

		Assert.That(result.Success, Is.True);
		var folder = _cache.GetFolderById(_folderAId)!;
		Assert.Multiple(() =>
		{
			Assert.That(folder.Widgets.Single(w => w.Id == w0.Id).IsPinned, Is.False);
			Assert.That(folder.Widgets.Single(w => w.Id == w0.Id).PinScope, Is.EqualTo(PinScope.Profile));
			Assert.That(folder.Widgets.Single(w => w.Id == w1.Id).IsPinned, Is.False);
			Assert.That(folder.Widgets.Single(w => w.Id == w1.Id).PinScope, Is.EqualTo(PinScope.Profile));
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
