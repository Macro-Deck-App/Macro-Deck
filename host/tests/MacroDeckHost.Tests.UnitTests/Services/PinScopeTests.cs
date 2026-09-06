using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class PinScopeTests
{
	private PinScopeFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new PinScopeFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();


	[Test]
	public async Task A1_SubtreePin_BlocksInsideTheSubtree_ButNotOutside()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		var insideSubtree = await _fixture.WidgetService.Create(_fixture.Retro.Id, Widget(0, 0));
		var outsideSubtree = await _fixture.WidgetService.Create(_fixture.Mail.Id, Widget(0, 0));

		Assert.Multiple(() =>
		{
			Assert.That(insideSubtree.Success, Is.False);
			Assert.That(insideSubtree.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(outsideSubtree.Success, Is.True);
		});
		Assert.That(_fixture.Reload(_fixture.Mail).Widgets.Single().PositionX, Is.EqualTo(0));
		PinScopeInvariants.AssertDeckIsRenderable(_fixture.FolderCache, _fixture.Cache, _fixture.ProfileId);
	}

	[Test]
	public async Task A2_ProfilePin_StillBlocksEveryFolder()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Profile);

		var result = await _fixture.WidgetService.Create(_fixture.Mail.Id, Widget(0, 0));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task A3_SubtreeIsRootedAtHome_NotAtTheParent()
	{
		var v = _fixture.AddWidget(_fixture.Games, 2, 2);
		await _fixture.WidgetService.SetPinned(_fixture.Games.Id, v.Id, true, PinScope.Subtree);

		var main = await _fixture.WidgetService.Create(_fixture.Main.Id, Widget(2, 2));
		var media = await _fixture.WidgetService.Create(_fixture.Media.Id, Widget(2, 2));
		var retro = await _fixture.WidgetService.Create(_fixture.Retro.Id, Widget(2, 2));

		Assert.Multiple(() =>
		{
			Assert.That(main.Success, Is.True, "Main is the pin's grandparent, not reached");
			Assert.That(media.Success, Is.True, "Media is a sibling of the home folder, not reached");
			Assert.That(retro.Success, Is.False, "Retro is a child of the home folder, reached");
			Assert.That(retro.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
		PinScopeInvariants.AssertDeckIsRenderable(_fixture.FolderCache, _fixture.Cache, _fixture.ProfileId);
	}

	[Test]
	public async Task A4_SubtreePin_CannotBeMoved()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		var result = await _fixture.WidgetService.UpdatePositions(_fixture.Main.Id,
			[new WidgetPlacement(w.Id, 2, 2, 1, 1)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.WidgetPinned));
		});
		var stored = _fixture.Reload(_fixture.Main).Widgets.Single();
		Assert.That((stored.PositionX, stored.PositionY), Is.EqualTo((0, 0)));
	}

	[Test]
	public async Task A5_UpdatePositions_OnlyGathersCellsFromReachingPins()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		var x = _fixture.AddWidget(_fixture.Mail, 3, 3);
		var y = _fixture.AddWidget(_fixture.Media, 3, 3);

		var outOfReach = await _fixture.WidgetService.UpdatePositions(_fixture.Mail.Id,
			[new WidgetPlacement(x.Id, 0, 0, 1, 1)]);
		var inReach = await _fixture.WidgetService.UpdatePositions(_fixture.Media.Id,
			[new WidgetPlacement(y.Id, 0, 0, 1, 1)]);

		Assert.Multiple(() =>
		{
			Assert.That(outOfReach.Success, Is.True);
			Assert.That(inReach.Success, Is.False);
			Assert.That(inReach.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
		Assert.That((_fixture.Reload(_fixture.Mail).Widgets.Single().PositionX,
				_fixture.Reload(_fixture.Mail).Widgets.Single().PositionY),
			Is.EqualTo((0, 0)));
		Assert.That((_fixture.Reload(_fixture.Media).Widgets.Single(w2 => w2.Id == y.Id).PositionX,
				_fixture.Reload(_fixture.Media).Widgets.Single(w2 => w2.Id == y.Id).PositionY),
			Is.EqualTo((3, 3)));
	}


	[Test]
	public async Task B1_SubtreePin_IgnoresAConflictOutsideItsReach()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		Assert.That(result.Success, Is.True);
		var stored = _fixture.Reload(_fixture.Main).Widgets.Single(x => x.Id == w.Id);
		Assert.Multiple(() =>
		{
			Assert.That(stored.IsPinned, Is.True);
			Assert.That(stored.PinScope, Is.EqualTo(PinScope.Subtree));
		});
		PinScopeInvariants.AssertDeckIsRenderable(_fixture.FolderCache, _fixture.Cache, _fixture.ProfileId);
	}

	[Test]
	public async Task B2_SubtreePin_NamesOnlyTheInReachConflict()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		_fixture.AddWidget(_fixture.Retro, 0, 0);
		_fixture.AddWidget(_fixture.Mail, 0, 0);
		var before = PinScopeInvariants.Snapshot(_fixture.FolderCache, _fixture.ProfileId);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Retro"));
			Assert.That(result.ErrorMessage, Does.Not.Contain("Mail"));
		});
		PinScopeInvariants.AssertUnchanged(before, _fixture.FolderCache, _fixture.ProfileId);
	}

	[Test]
	public async Task B3_SubtreePin_RejectedWhenItDoesNotFitASmallerDescendantGrid()
	{
		var media = _fixture.Media;
		media.Columns = 2;
		media.Rows = 2;
		await _fixture.FolderCache.AddOrUpdate(media);
		var z = _fixture.AddWidget(_fixture.Main, 3, 3);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, z.Id, true, PinScope.Subtree);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Media"));
		});
		Assert.That((_fixture.Reload(_fixture.Media).Columns, _fixture.Reload(_fixture.Media).Rows),
			Is.EqualTo(((int?)2, (int?)2)));
	}

	[Test]
	public async Task B4_SubtreePin_FromALeaf_NeverFallsBackToEveryFolder()
	{
		var m = _fixture.AddWidget(_fixture.Mail, 3, 3);
		_fixture.AddWidget(_fixture.Main, 3, 3);
		_fixture.AddWidget(_fixture.Games, 3, 3);
		_fixture.AddWidget(_fixture.Retro, 3, 3);
		_fixture.AddWidget(_fixture.Media, 3, 3);
		var games = _fixture.Games;
		games.Columns = 2;
		games.Rows = 2;
		await _fixture.FolderCache.AddOrUpdate(games);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Mail.Id, m.Id, true, PinScope.Subtree);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task B5_ProfilePin_StillValidatesEveryFolder()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Profile);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Mail"));
		});
	}

	[Test]
	public async Task B7_WideningPin_SeesTheDisplayFootprintOfANarrowerExistingPin()
	{
		var w2 = _fixture.AddWidget(_fixture.Games, 0, 0);
		var p1 = _fixture.AddWidget(_fixture.Mail, 0, 0);
		var pinW2 = await _fixture.WidgetService.SetPinned(_fixture.Games.Id, w2.Id, true, PinScope.Subtree);
		Assert.That(pinW2.Success, Is.True);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Mail.Id, p1.Id, true, PinScope.Profile);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Games"));
		});
	}

	[Test]
	public async Task B8_TwoIndependentSubtreePins_OnTheSameCell_BothSucceed()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		var m = _fixture.AddWidget(_fixture.Mail, 0, 0);

		var pinW = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		var pinM = await _fixture.WidgetService.SetPinned(_fixture.Mail.Id, m.Id, true, PinScope.Subtree);

		Assert.Multiple(() =>
		{
			Assert.That(pinW.Success, Is.True);
			Assert.That(pinM.Success, Is.True);
			Assert.That(pinW.Data!.PinScope, Is.EqualTo(PinScope.Subtree));
			Assert.That(pinM.Data!.PinScope, Is.EqualTo(PinScope.Subtree));
		});
		PinScopeInvariants.AssertDeckIsRenderable(_fixture.FolderCache, _fixture.Cache, _fixture.ProfileId);
	}


	[Test]
	public async Task C1_NarrowingToSubtree_IsNotASilentNoOp()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Profile);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.PinScope, Is.EqualTo(PinScope.Subtree));
			Assert.That(result.Data!.IsPinned, Is.True);
			Assert.That((result.Data!.PositionX, result.Data!.PositionY), Is.EqualTo((0, 0)));
		});
		var create = await _fixture.WidgetService.Create(_fixture.Mail.Id, Widget(0, 0));
		Assert.That(create.Success, Is.True);
	}

	[Test]
	public async Task C2_WideningToProfile_ValidatesAndIsRejectedOnConflict()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Profile);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.PositionOccupied));
			Assert.That(result.ErrorMessage, Does.Contain("Mail"));
		});
		var stored = _fixture.Reload(_fixture.Main).Widgets.Single(x => x.Id == w.Id);
		Assert.Multiple(() =>
		{
			Assert.That(stored.PinScope, Is.EqualTo(PinScope.Subtree));
			Assert.That(stored.IsPinned, Is.True);
		});
	}

	[Test]
	public async Task C3_ReassertingTheCurrentScope_Succeeds()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.PinScope, Is.EqualTo(PinScope.Subtree));
		});
		Assert.That((_fixture.Reload(_fixture.Mail).Widgets.Single().PositionX,
				_fixture.Reload(_fixture.Mail).Widgets.Single().PositionY),
			Is.EqualTo((0, 0)));
	}

	[Test]
	public async Task C4_Unpinning_AlwaysSucceedsEvenWithAConflictElsewhere()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.IsPinned, Is.False);
			Assert.That(result.Data!.PinScope, Is.EqualTo(PinScope.Profile));
		});
	}

	// --- Field-preservation regression: a plain widget update must not silently widen a pin's scope ---

	[Test]
	public async Task PlainUpdate_PreservesPinScope_AndDoesNotWidenReach()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		var pinned = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		Assert.That(pinned.Success, Is.True);

		var updated = new Domain.Entities.WidgetEntity
		{
			Id = w.Id,
			FolderId = _fixture.Main.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			Data = "{\"label\":\"changed\"}"
		};
		var updateResult = await _fixture.WidgetService.Update(updated);

		Assert.That(updateResult.Success, Is.True);
		var stored = _fixture.Reload(_fixture.Main).Widgets.Single(x => x.Id == w.Id);
		Assert.Multiple(() =>
		{
			Assert.That(stored.IsPinned, Is.True);
			Assert.That(stored.PinScope, Is.EqualTo(PinScope.Subtree));
		});

		var outsideSubtree = await _fixture.WidgetService.Create(_fixture.Mail.Id, Widget(0, 0));
		Assert.That(outsideSubtree.Success, Is.True);
	}


	[Test]
	public async Task F1_PushedWidgetDto_CarriesTheScope()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		var dto = Application.Ui.Handlers.FolderDtoMapper.MapWidgetToDto(result.Data!);
		Assert.Multiple(() =>
		{
			Assert.That(dto.IsPinned, Is.True);
			Assert.That(dto.PinScope, Is.EqualTo(PinScope.Subtree));
		});
	}

	[Test]
	public async Task F2_ScopeOnlyChange_StillPublishesAnUpdateNotification()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Profile);
		_fixture.Mediator.Published.Clear();

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		Assert.That(result.Success, Is.True);
		var notification = _fixture.Mediator.Published.OfType<WidgetUpdatedNotification>().Single();
		Assert.That(notification.Widget.PinScope, Is.EqualTo(PinScope.Subtree));
	}

	[Test]
	public async Task F3_AbsentScopeOnAFreshPin_RespondsWithTheEffectiveProfileScope()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);

		var result = await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.PinScope, Is.EqualTo(PinScope.Profile));
		});
	}

	private static Domain.Entities.WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};
}
