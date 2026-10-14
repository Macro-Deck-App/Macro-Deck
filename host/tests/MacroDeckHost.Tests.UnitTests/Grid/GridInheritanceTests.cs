using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Grid;

[TestFixture]
public class GridInheritanceTests
{
	[Test]
	public void ResolveRows_OwnValueWins()
	{
		var folder = Folder(7);

		var resolved = GridInheritance.ResolveRows(folder, [folder], 3);

		Assert.That(resolved, Is.EqualTo(7));
	}

	[Test]
	public void ResolveColumns_NearestAncestorBeatsAFurtherOne()
	{
		var grandparent = Folder(columns: 9);
		var parent = Folder(columns: 6, parentId: grandparent.Id);
		var child = Folder(columns: null, parentId: parent.Id);

		var resolved = GridInheritance.ResolveColumns(child, [grandparent, parent, child], 3);

		Assert.That(resolved, Is.EqualTo(6));
	}

	[Test]
	public void ResolveRows_RootFallsBackToTheProfileDefault()
	{
		var root = Folder(null);

		var resolved = GridInheritance.ResolveRows(root, [root], 9);

		Assert.That(resolved, Is.EqualTo(9));
	}

	[Test]
	public void ResolveColumns_UnknownFolderFallsBackToGridDefaults()
	{
		var folder = Folder(columns: null);

		var resolved = GridInheritance.ResolveColumns(folder, [], GridDefaults.Columns);

		Assert.That(resolved, Is.EqualTo(GridDefaults.Columns));
	}

	[Test]
	public void ResolveRows_AParentIdCycleFallsThroughToTheProfileDefault()
	{
		var a = Folder(null);
		var b = Folder(null, parentId: a.Id);
		a.ParentId = b.Id; // a -> b -> a

		var resolved = GridInheritance.ResolveRows(a, [a, b], 4);

		Assert.That(resolved, Is.EqualTo(4));
	}

	[Test]
	public void WithCandidate_LeavesTheInputFolderUnmutated()
	{
		var folder = Folder(4, 4);

		var candidate = GridInheritance.WithCandidate([folder], folder.Id, 8, 8, null);

		Assert.Multiple(() =>
		{
			Assert.That(folder.Rows, Is.EqualTo(4));
			Assert.That(folder.Columns, Is.EqualTo(4));
			Assert.That(candidate.Single().Rows, Is.EqualTo(8));
			Assert.That(candidate.Single().Columns, Is.EqualTo(8));
			Assert.That(candidate.Single(), Is.Not.SameAs(folder));
		});
	}

	[Test]
	public void WithCandidate_ReusesTheSameWidgetsListReference()
	{
		var folder = Folder(4, 4);
		folder.Widgets.Add(Widget(0, 0));

		var candidate = GridInheritance.WithCandidate([folder], folder.Id, 2, 2, null);

		Assert.That(candidate.Single().Widgets, Is.SameAs(folder.Widgets));
	}

	[Test]
	public void WithCandidate_ReparentsSoAnInheritedGridResolvesThroughTheNewAncestor()
	{
		var small = Folder(2, 2);
		var big = Folder(9, 9);
		var child = Folder(null, null, small.Id);
		List<FolderEntity> folders = [small, big, child];

		var candidate = GridInheritance.WithCandidate(folders, child.Id, null, null, big.Id);

		var moved = candidate.Single(f => f.Id == child.Id);
		Assert.Multiple(() =>
		{
			Assert.That(GridInheritance.ResolveRows(moved, candidate, 3), Is.EqualTo(9));
			Assert.That(child.ParentId, Is.EqualTo(small.Id)); // the cached entity is untouched
		});
	}

	[Test]
	public void FindGridConflicts_FindsAnInheritingDescendant()
	{
		var parent = Folder(2, 2);
		var child = Folder(null, null, parent.Id);
		child.Widgets.Add(Widget(3, 3)); // needs 4 columns, 4 rows; parent (and so the child) resolves to 2x2

		var conflicts =
			GridInheritance.FindGridConflicts([parent, child], 3, 3);

		Assert.That(conflicts.Select(conflict => conflict.Folder.Id), Is.EqualTo(new[] { child.Id }));
	}

	[Test]
	public void FindGridConflicts_NothingWhenEveryFolderPinsItsOwnGrid()
	{
		var folder = Folder(1, 1);
		folder.Widgets.Add(Widget(0, 0));

		var conflicts = GridInheritance.FindGridConflicts([folder], 3, 3);

		Assert.That(conflicts, Is.Empty);
	}

	[Test]
	public void FindGridConflicts_ReportsEachFoldersOwnRequirement()
	{
		var small = Folder(1, 1);
		small.Widgets.Add(Widget(2, 4)); // requires 3 columns, 5 rows

		var conflicts = GridInheritance.FindGridConflicts([small], 3, 3);

		var conflict = conflicts.Single();
		Assert.Multiple(() =>
		{
			Assert.That(conflict.RequiredColumns, Is.EqualTo(3));
			Assert.That(conflict.RequiredRows, Is.EqualTo(5));
		});
	}

	[Test]
	public void NewConflicts_OnlyReturnsFoldersNotAlreadyConflicting()
	{
		var stillBroken = Folder(1, 1);
		var newlyBroken = Folder(1, 1);
		var current = new List<GridConflict> { new(stillBroken, 2, 2) };
		var candidate = new List<GridConflict> { new(stillBroken, 2, 2), new(newlyBroken, 2, 2) };

		var result = GridInheritance.NewConflicts(current, candidate);

		Assert.That(result.Select(conflict => conflict.Folder.Id), Is.EqualTo(new[] { newlyBroken.Id }));
	}

	[Test]
	public void DescribeConflicts_JoinsOneClausePerFolder()
	{
		var games = Folder(1, 1);
		games.Name = "Games";
		var conflicts = new List<GridConflict> { new(games, 5, 3) };

		var message = GridInheritance.DescribeConflicts(conflicts);

		Assert.That(message,
			Is.EqualTo("Games needs at least 5 x 3 to keep every widget (including pinned ones) inside"));
	}

	private static FolderEntity Folder(int? rows = null, int? columns = null, Guid? parentId = null)
	{
		return new FolderEntity
		{
			Id = Guid.NewGuid(),
			Name = "F",
			Order = 0,
			ParentId = parentId,
			Rows = rows,
			Columns = columns
		};
	}

	private static WidgetEntity Widget(int x, int y)
	{
		return new WidgetEntity
		{
			Id = Guid.NewGuid(),
			Type = WidgetTypeIds.ActionButton,
			PositionX = x,
			PositionY = y,
			Width = 1,
			Height = 1
		};
	}
}
