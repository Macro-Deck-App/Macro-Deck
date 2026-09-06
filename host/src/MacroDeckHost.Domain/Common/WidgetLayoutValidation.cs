using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Domain.Common;

public static class WidgetLayoutValidation
{
	public static bool HasOverlap(
		FolderEntity folder,
		IReadOnlyList<WidgetEntity> foreignPinned,
		IReadOnlyList<WidgetPlacement> moved,
		IReadOnlyList<WidgetPlacement> added,
		IReadOnlySet<Guid> removed)
	{
		var touchedIds = moved.Select(p => p.WidgetId)
			.Concat(added.Select(p => p.WidgetId))
			.ToHashSet();

		var finalRects = folder.Widgets
			.Where(w => !removed.Contains(w.Id))
			.Select(w =>
				moved.FirstOrDefault(p => p.WidgetId == w.Id) ??
				new WidgetPlacement(w.Id, w.PositionX, w.PositionY, w.Width, w.Height))
			.Concat(added)
			.Concat(foreignPinned.Select(w =>
				new WidgetPlacement(w.Id, w.PositionX, w.PositionY, w.Width, w.Height)))
			.ToList();

		for (var i = 0; i < finalRects.Count; i++)
		{
			for (var j = i + 1; j < finalRects.Count; j++)
			{
				var a = finalRects[i];
				var b = finalRects[j];
				if (!touchedIds.Contains(a.WidgetId) && !touchedIds.Contains(b.WidgetId))
				{
					continue;
				}

				var overlaps = a.X < b.X + b.Width &&
					a.X + a.Width > b.X &&
					a.Y < b.Y + b.Height &&
					a.Y + a.Height > b.Y;
				if (overlaps)
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool OutOfBounds(WidgetPlacement placement, int columns, int rows)
	{
		return placement.Width < 1 ||
			placement.Height < 1 ||
			placement.X < 0 ||
			placement.Y < 0 ||
			placement.X + placement.Width > columns ||
			placement.Y + placement.Height > rows;
	}
}
