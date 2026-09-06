namespace MacroDeckHost.Application.Portable;

public readonly record struct PortableCell(int X, int Y, int Width, int Height);

public static class PortableWidgetPlacement
{
	public static IReadOnlyList<(int X, int Y)>? Place(IReadOnlyList<PortableCell> group,
		int columns,
		int rows,
		IReadOnlyList<PortableCell> occupied,
		int anchorX,
		int anchorY)
	{
		if (group.Count == 0)
		{
			return [];
		}

		var boundingWidth = group.Max(cell => cell.X + cell.Width);
		var boundingHeight = group.Max(cell => cell.Y + cell.Height);
		if (boundingWidth > columns || boundingHeight > rows)
		{
			return null;
		}

		foreach (var (offsetX, offsetY) in CandidateOffsets(columns,
			rows,
			boundingWidth,
			boundingHeight,
			anchorX,
			anchorY))
		{
			if (Fits(group, occupied, offsetX, offsetY))
			{
				return group.Select(cell => (offsetX + cell.X, offsetY + cell.Y)).ToList();
			}
		}

		return null;
	}

	private static IEnumerable<(int X, int Y)> CandidateOffsets(int columns,
		int rows,
		int boundingWidth,
		int boundingHeight,
		int anchorX,
		int anchorY)
	{
		var maxX = columns - boundingWidth;
		var maxY = rows - boundingHeight;
		var clampedAnchorX = Math.Clamp(anchorX, 0, maxX);
		var clampedAnchorY = Math.Clamp(anchorY, 0, maxY);

		yield return (clampedAnchorX, clampedAnchorY);

		for (var y = 0; y <= maxY; y++)
		{
			for (var x = 0; x <= maxX; x++)
			{
				if (x == clampedAnchorX && y == clampedAnchorY)
				{
					continue;
				}

				yield return (x, y);
			}
		}
	}

	private static bool Fits(IReadOnlyList<PortableCell> group,
		IReadOnlyList<PortableCell> occupied,
		int offsetX,
		int offsetY)
	{
		foreach (var cell in group)
		{
			var placed = new PortableCell(offsetX + cell.X, offsetY + cell.Y, cell.Width, cell.Height);
			foreach (var other in occupied)
			{
				if (Overlaps(placed, other))
				{
					return false;
				}
			}
		}

		return true;
	}

	private static bool Overlaps(PortableCell a, PortableCell b)
		=> a.X < b.X + b.Width &&
			a.X + a.Width > b.X &&
			a.Y < b.Y + b.Height &&
			a.Y + a.Height > b.Y;
}
