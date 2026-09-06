using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Common;

public static class PinnedWidgetLayout
{
	public static IEnumerable<WidgetEntity> DisplayedWidgets(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders)
		=> FolderChain.DisplayedWidgets<FolderEntity, Guid, WidgetEntity>(folder,
			profileFolders,
			FolderEntityChain.IdOf,
			FolderEntityChain.ParentIdOf,
			FolderEntityChain.WidgetsOf,
			widget => widget.IsPinned,
			widget => widget.PinScope == PinScope.Profile);

	public static IEnumerable<WidgetEntity> PinnedWidgetsOutside(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders)
		=> FolderChain.PinnedWidgetsOutside<FolderEntity, Guid, WidgetEntity>(folder,
			profileFolders,
			FolderEntityChain.IdOf,
			FolderEntityChain.ParentIdOf,
			FolderEntityChain.WidgetsOf,
			widget => widget.IsPinned,
			widget => widget.PinScope == PinScope.Profile);

	public static void GrowToFitPinned(
		FolderEntity folder,
		IEnumerable<FolderEntity> profileFolders,
		int effectiveColumns,
		int effectiveRows)
	{
		var pinned = PinnedWidgetsOutside(folder, profileFolders).ToList();
		if (pinned.Count == 0)
		{
			return;
		}

		var requiredColumns = pinned.Max(w => w.PositionX + w.Width);
		if (requiredColumns > effectiveColumns)
		{
			folder.Columns = requiredColumns;
		}

		var requiredRows = pinned.Max(w => w.PositionY + w.Height);
		if (requiredRows > effectiveRows)
		{
			folder.Rows = requiredRows;
		}
	}

	public static int RequiredColumns(FolderEntity folder, IEnumerable<FolderEntity> profileFolders)
	{
		return DisplayedWidgets(folder, profileFolders)
			.Select(w => w.PositionX + w.Width)
			.DefaultIfEmpty(1)
			.Max();
	}

	public static int RequiredRows(FolderEntity folder, IEnumerable<FolderEntity> profileFolders)
	{
		return DisplayedWidgets(folder, profileFolders)
			.Select(w => w.PositionY + w.Height)
			.DefaultIfEmpty(1)
			.Max();
	}

	public static List<FolderEntity> FindPinConflicts(
		WidgetEntity widget,
		FolderEntity homeFolder,
		PinScope candidateScope,
		IReadOnlyCollection<FolderEntity> profileFolders,
		int profileDefaultRows,
		int profileDefaultColumns)
	{
		var conflicts = new List<FolderEntity>();
		var candidateFolders = candidateScope == PinScope.Subtree
			? FolderSubtree.Collect(profileFolders, homeFolder)
			: profileFolders;

		foreach (var folder in candidateFolders)
		{
			var columns = GridInheritance.ResolveColumns(folder, profileFolders, profileDefaultColumns);
			var rows = GridInheritance.ResolveRows(folder, profileFolders, profileDefaultRows);
			if (widget.PositionX < 0 ||
				widget.PositionY < 0 ||
				widget.PositionX + widget.Width > columns ||
				widget.PositionY + widget.Height > rows)
			{
				conflicts.Add(folder);
				continue;
			}

			if (DisplayedWidgets(folder, profileFolders)
				.Any(other => other.Id != widget.Id && Overlaps(widget, other)))
			{
				conflicts.Add(folder);
			}
		}

		return conflicts;
	}

	public static List<PinReachConflict> FindNewlyReachedOverlaps(
		IReadOnlyList<FolderEntity> current,
		IReadOnlyList<FolderEntity> candidate)
	{
		var currentById = current.ToDictionary(f => f.Id);
		var conflicts = new List<PinReachConflict>();

		foreach (var folder in candidate)
		{
			if (!currentById.TryGetValue(folder.Id, out var previousFolder))
			{
				continue;
			}

			var previouslyReachingIds = PinnedWidgetsOutside(previousFolder, current).Select(w => w.Id).ToHashSet();
			var newlyReaching = PinnedWidgetsOutside(folder, candidate)
				.Where(w => !previouslyReachingIds.Contains(w.Id))
				.ToList();
			if (newlyReaching.Count == 0)
			{
				continue;
			}

			var displayed = DisplayedWidgets(folder, candidate).ToList();
			var conflicting = newlyReaching.FirstOrDefault(pin =>
				displayed.Any(other => other.Id != pin.Id && Overlaps(pin, other)));
			if (conflicting is not null)
			{
				var home = candidate.First(f => f.Widgets.Any(w => w.Id == conflicting.Id));
				conflicts.Add(new PinReachConflict(folder, home));
			}
		}

		return conflicts;
	}

	public static string DescribeReachConflicts(IEnumerable<PinReachConflict> conflicts)
		=> string.Join("; ",
			conflicts.Select(conflict =>
				$"{conflict.Folder.Name} would overlap the pinned widget from {conflict.Home.Name}"));

	public static bool Overlaps(WidgetEntity a, WidgetEntity b)
	{
		return a.PositionX < b.PositionX + b.Width &&
			a.PositionX + a.Width > b.PositionX &&
			a.PositionY < b.PositionY + b.Height &&
			a.PositionY + a.Height > b.PositionY;
	}
}

public sealed record PinReachConflict(FolderEntity Folder, FolderEntity Home);
