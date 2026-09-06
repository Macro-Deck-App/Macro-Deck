using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class PinScopeFolderTests
{
	private PinScopeFixture _fixture = null!;

	[SetUp]
	public void SetUp() => _fixture = new PinScopeFixture();

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task D1_Move_Inside_IntoASubtreePinsReach_IsRejectedAndInert()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		_fixture.AddWidget(_fixture.Mail, 0, 0);
		var before = PinScopeInvariants.Snapshot(_fixture.FolderCache, _fixture.ProfileId);

		var result = await _fixture.FolderService.Move(_fixture.Mail.Id, _fixture.Main.Id, FolderMovePosition.Inside);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.PinnedWidgetConflict));
			Assert.That(result.ErrorMessage, Does.Contain("Mail"));
			Assert.That(_fixture.Reload(_fixture.Mail).ParentId, Is.EqualTo(_fixture.Work.Id));
		});
		PinScopeInvariants.AssertUnchanged(before, _fixture.FolderCache, _fixture.ProfileId);
	}

	[Test]
	public async Task D2_Move_After_IntoASubtreePinsReach_IsRejectedTheSameWay()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.FolderService.Move(_fixture.Mail.Id, _fixture.Games.Id, FolderMovePosition.After);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.PinnedWidgetConflict));
			Assert.That(_fixture.Reload(_fixture.Mail).ParentId, Is.EqualTo(_fixture.Work.Id));
		});
	}

	[Test]
	public async Task D3_Move_OnlyGridTooSmall_IsStillRejectedByTheExistingGridCheck()
	{
		var z = _fixture.AddWidget(_fixture.Main, 3, 3);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, z.Id, true, PinScope.Subtree);
		var small = _fixture.AddFolder("Small", null, rows: 2, columns: 2);

		var result = await _fixture.FolderService.Move(small.Id, _fixture.Main.Id, FolderMovePosition.Inside);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.ErrorMessage, Does.Contain("Small"));
		});
		var reloaded = _fixture.Reload(small);
		Assert.Multiple(() =>
		{
			Assert.That((reloaded.Columns, reloaded.Rows), Is.EqualTo(((int?)2, (int?)2)));
			Assert.That(reloaded.ParentId, Is.Null);
		});
	}

	[Test]
	public async Task D4_MovingTheHomeSubtreeOut_IsNeverOverRejected_AndReachIsNotCached()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		var result = await _fixture.FolderService.Move(_fixture.Games.Id, _fixture.Work.Id, FolderMovePosition.After);

		Assert.That(result.Success, Is.True);
		var games = await _fixture.WidgetService.Create(_fixture.Games.Id, Widget(0, 0));
		var retro = await _fixture.WidgetService.Create(_fixture.Retro.Id, Widget(0, 0));
		Assert.Multiple(() =>
		{
			Assert.That(games.Success, Is.True);
			Assert.That(retro.Success, Is.True, "Retro moved out along with Games");
		});
	}

	[Test]
	public async Task D5_ReachIsRecomputedAfterAMove_NotCachedFromPinTime()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);

		var move = await _fixture.FolderService.Move(_fixture.Mail.Id, _fixture.Main.Id, FolderMovePosition.Inside);
		Assert.That(move.Success, Is.True);

		var create = await _fixture.WidgetService.Create(_fixture.Mail.Id, Widget(0, 0));
		Assert.Multiple(() =>
		{
			Assert.That(create.Success, Is.False);
			Assert.That(create.Error, Is.EqualTo(WidgetError.PositionOccupied));
		});
	}

	[Test]
	public async Task D6_ProfilePin_NeverInventsAReachConflictOnAMove()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Profile);

		var result = await _fixture.FolderService.Move(_fixture.Mail.Id, _fixture.Main.Id, FolderMovePosition.Inside);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task D7_Update_SameReparentGuard_AsMove()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var result = await _fixture.FolderService.Update(_fixture.Mail.Id,
			null,
			_fixture.Main.Id,
			null,
			null,
			null,
			null,
			null,
			null,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(FolderError.PinnedWidgetConflict));
			Assert.That(_fixture.Reload(_fixture.Mail).ParentId, Is.EqualTo(_fixture.Work.Id));
		});
	}

	[Test]
	public async Task D8_SubtreePinOutOfReach_DoesNotBlockAGridReduction()
	{
		var z = _fixture.AddWidget(_fixture.Main, 3, 3);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, z.Id, true, PinScope.Subtree);

		var result = await _fixture.FolderService.Update(_fixture.Mail.Id,
			null,
			null,
			null,
			2,
			2,
			null,
			null,
			null,
			null);

		Assert.That(result.Success, Is.True);
		Assert.That((_fixture.Reload(_fixture.Mail).Columns, _fixture.Reload(_fixture.Mail).Rows),
			Is.EqualTo(((int?)2, (int?)2)));
	}

	[Test]
	public async Task D9_SubtreePinInReach_StillBlocksAGridReduction()
	{
		var z = _fixture.AddWidget(_fixture.Main, 3, 3);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, z.Id, true, PinScope.Subtree);

		var result = await _fixture.FolderService.Update(_fixture.Retro.Id,
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
		Assert.That((_fixture.Reload(_fixture.Retro).Columns, _fixture.Reload(_fixture.Retro).Rows),
			Is.EqualTo(((int?)4, (int?)4)));
	}

	[Test]
	public async Task D10_FolderCreate_GrowsInsideTheReach_ButNotAnUnrelatedNewRoot()
	{
		var profile = _fixture.Cache.GetById(_fixture.ProfileId)!;
		profile.DefaultRows = 2;
		profile.DefaultColumns = 2;
		await _fixture.Cache.AddOrUpdate(profile);

		var z = _fixture.AddWidget(_fixture.Main, 3, 3);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, z.Id, true, PinScope.Subtree);

		var child = await _fixture.FolderService.Create(_fixture.ProfileId, "Child", _fixture.Main.Id);
		Assert.That(child.Success, Is.True);
		var afterChild = _fixture.FolderCache.GetFoldersByProfileId(_fixture.ProfileId);
		var childColumns = GridInheritance.ResolveColumns(child.Data!, afterChild, profile.DefaultColumns);
		var childRows = GridInheritance.ResolveRows(child.Data!, afterChild, profile.DefaultRows);

		var root2 = await _fixture.FolderService.Create(_fixture.ProfileId, "Root2", null);
		Assert.That(root2.Success, Is.True);
		var afterRoot2 = _fixture.FolderCache.GetFoldersByProfileId(_fixture.ProfileId);
		var root2Columns = GridInheritance.ResolveColumns(root2.Data!, afterRoot2, profile.DefaultColumns);
		var root2Rows = GridInheritance.ResolveRows(root2.Data!, afterRoot2, profile.DefaultRows);

		Assert.Multiple(() =>
		{
			Assert.That((childColumns, childRows), Is.EqualTo((4, 4)), "Child is inside the pin's reach");
			Assert.That((root2Columns, root2Rows), Is.EqualTo((2, 2)), "Root2 is an unrelated new root");
			Assert.That((root2.Data!.Columns, root2.Data!.Rows),
				Is.EqualTo(((int?)null, (int?)null)),
				"Root2 must not be explicitly grown");
		});
	}

	[Test]
	public async Task D11_Duplicate_DoesNotDoubleAWidgetThePinAlreadyRendersOnTheCopy()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Main.Id, w.Id, true, PinScope.Subtree);
		var plain = _fixture.AddWidget(_fixture.Games, 1, 1);

		var result = await _fixture.FolderService.Duplicate(_fixture.Games.Id);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Widgets, Has.Count.EqualTo(1));
			Assert.That(result.Data!.Widgets.Single().Id, Is.Not.EqualTo(w.Id));
			Assert.That(result.Data!.Widgets.Single().PositionX, Is.EqualTo(plain.PositionX));
		});
	}

	[Test]
	public async Task D12_Duplicate_ASubtreePinInTheHomeFolder_IsGenuinelyCopiedBecauseItDoesNotReachASibling()
	{
		var w = _fixture.AddWidget(_fixture.Games, 0, 0);
		await _fixture.WidgetService.SetPinned(_fixture.Games.Id, w.Id, true, PinScope.Subtree);

		var result = await _fixture.FolderService.Duplicate(_fixture.Games.Id);

		Assert.That(result.Success, Is.True);
		var copy = result.Data!.Widgets.Single();
		Assert.Multiple(() =>
		{
			Assert.That(copy.Id, Is.Not.EqualTo(w.Id));
			Assert.That(copy.IsPinned, Is.True);
			Assert.That(copy.PinScope, Is.EqualTo(PinScope.Subtree));
			Assert.That((copy.PositionX, copy.PositionY), Is.EqualTo((0, 0)));
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
