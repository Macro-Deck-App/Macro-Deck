using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class GridComponent
{
	private const int MaxCount = 64;

	private const double TieEpsilon = 1e-6;

	public static readonly ComponentDefinition Grid = new(Components.Grid, "div", Paint, 1, Intrinsic);

	private sealed record Placement(UiNode Child, int Row, int Column, int RowSpan, int ColumnSpan);

	private static int Count(UiNode node, string key)
	{
		return node.Number(key) is >= 1 and var value ? Math.Min(MaxCount, (int)Math.Floor(value)) : 1;
	}

	private static (List<Placement> Placements, int Columns, int Rows) Place(UiNode node, int? chosenColumns)
	{
		var columns = chosenColumns ?? Count(node, "columns");
		var declaredRows = node.Number("rows");
		int? rowLimit = chosenColumns is null && declaredRows is >= 0
			? Math.Min(MaxCount, (int)Math.Floor(declaredRows.Value))
			: null;
		var taken = new Dictionary<(int, int), bool>();

		bool Free(int row, int column, int rowSpan, int columnSpan)
		{
			for (var r = row; r < row + rowSpan; r++)
			{
				for (var c = column; c < column + columnSpan; c++)
				{
					if (taken.ContainsKey((r, c)))
					{
						return false;
					}
				}
			}

			return true;
		}

		var placements = new List<Placement>();
		var usedRows = 0;
		foreach (var child in node.Children)
		{
			var columnSpan = Math.Min(Count(child, "columnSpan"), columns);
			var rowSpan = Count(child, "rowSpan");
			Placement? placed = null;
			for (var row = 0; placed is null && (rowLimit is null || row + rowSpan <= rowLimit); row++)
			{
				for (var column = 0; column + columnSpan <= columns; column++)
				{
					if (Free(row, column, rowSpan, columnSpan))
					{
						placed = new Placement(child, row, column, rowSpan, columnSpan);
						break;
					}
				}

				if (row > MaxCount * MaxCount)
				{
					break;
				}
			}

			if (placed is null)
			{
				continue;
			}

			for (var r = placed.Row; r < placed.Row + rowSpan; r++)
			{
				for (var c = placed.Column; c < placed.Column + columnSpan; c++)
				{
					taken[(r, c)] = true;
				}
			}

			usedRows = Math.Max(usedRows, placed.Row + rowSpan);
			placements.Add(placed);
		}

		return (placements, columns, rowLimit ?? usedRows);
	}

	private static int RowsFor(UiNode node, int columns)
	{
		var spans = node.Children.Any(child => Count(child, "columnSpan") > 1 || Count(child, "rowSpan") > 1);
		return spans ? Place(node, columns).Rows : (int)Math.Ceiling(node.Children.Count / (double)columns);
	}

	private static int? ChooseColumns(
		UiNode node,
		double? width,
		double? height,
		double padding,
		double gap,
		double? minCell)
	{
		if (minCell is not { } cell || !double.IsFinite(cell) || cell <= 0 || width is null)
		{
			return null;
		}

		var contentWidth = Math.Max(0, width.Value - 2 * padding);
		double? contentHeight = height is { } h ? Math.Max(0, h - 2 * padding) : null;
		var limit = Math.Min(MaxCount, Math.Max(1, node.Children.Count));

		double ColumnEdge(int columns)
		{
			return Math.Max(0, (contentWidth - (columns - 1) * gap) / columns);
		}

		var chosen = 1;
		if (contentHeight is null)
		{
			for (var columns = 2; columns <= limit; columns++)
			{
				if (ColumnEdge(columns) >= cell - TieEpsilon)
				{
					chosen = columns;
				}
			}

			return chosen;
		}

		var bestEdge = -1.0;
		for (var columns = 1; columns <= limit; columns++)
		{
			var rows = RowsFor(node, columns);
			var rowEdge = rows == 0 ? 0 : Math.Max(0, (contentHeight.Value - (rows - 1) * gap) / rows);
			var edge = Math.Min(ColumnEdge(columns), rowEdge);
			if (edge > bestEdge + TieEpsilon)
			{
				bestEdge = edge;
				chosen = columns;
			}
		}

		return chosen;
	}

	private static double IntrinsicHeight(UiNode node, double width, double padding, double gap, int? columns)
	{
		var (_, used, rows) = Place(node, columns);
		var cellWidth = Math.Max(0, (width - 2 * padding - (used - 1) * gap) / used);
		return rows == 0 ? 2 * padding : 2 * padding + rows * cellWidth + (rows - 1) * gap;
	}

	private static double Intrinsic(UiNode node, IntrinsicMetrics metrics)
	{
		if (metrics.Horizontal || metrics.CrossExtent is not { } cross)
		{
			return 0;
		}

		var padding = Layouts.Length(node, "padding", metrics.Scope);
		var gap = Layouts.Length(node, "gap", metrics.Scope);
		var minCell = UiLength.Resolve(UiLength.Of(node, "minCellSize"), metrics.Scope);
		return IntrinsicHeight(node, cross, padding, gap, ChooseColumns(node, cross, null, padding, gap, minCell));
	}

	private static void Paint(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-grid");
		var padding = Layouts.Length(node, "padding", scope.Scope);
		var gap = Layouts.Length(node, "gap", scope.Scope);
		var minCell = scope.Resolve("minCellSize");
		var box = scope.Box;
		var columns = ChooseColumns(node, box.Width, box.Height, padding, gap, minCell);
		NodeScope.SizeTo(element,
			box.Height is null && box.Width is { } width
				? new BoxSize(width, IntrinsicHeight(node, width, padding, gap, columns))
				: box);
		var (placements, used, rows) = Place(node, columns);
		var contentWidth = Math.Max(0, (box.Width ?? 0) - 2 * padding);
		double? contentHeight = box.Height is { } height ? Math.Max(0, height - 2 * padding) : null;
		var cellWidth = Math.Max(0, (contentWidth - (used - 1) * gap) / used);
		var cellHeight = contentHeight is { } inner
			? rows == 0 ? 0 : Math.Max(0, (inner - (rows - 1) * gap) / rows)
			: cellWidth;
		foreach (var placement in placements)
		{
			var cell = new BoxSize(placement.ColumnSpan * cellWidth + (placement.ColumnSpan - 1) * gap,
				placement.RowSpan * cellHeight + (placement.RowSpan - 1) * gap);
			var before = element.Children.Count;
			scope.SyncChildren(element, [new ChildEntry(placement.Child, cell, null)]);
			var child = element.Children[before];
			child.SetStyle("left", NodeScope.Px(padding + placement.Column * (cellWidth + gap)));
			child.SetStyle("top", NodeScope.Px(padding + placement.Row * (cellHeight + gap)));
		}

		scope.PressTint();
	}
}
