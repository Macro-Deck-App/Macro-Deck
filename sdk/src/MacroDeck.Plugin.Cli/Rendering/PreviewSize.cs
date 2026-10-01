using System.Globalization;

namespace MacroDeck.Plugin.Cli.Rendering;

internal readonly record struct PreviewSize(int Width, int Height)
{
	public static PreviewSize OfCells(int columns, int rows)
		=> new(columns * PreviewMetrics.CellSize + (columns - 1) * PreviewMetrics.CellGap,
			rows * PreviewMetrics.CellSize + (rows - 1) * PreviewMetrics.CellGap);

	public static bool TryParse(string text, bool cells, out PreviewSize size)
	{
		size = default;
		var parts = text.Split('x', 'X');

		if (parts.Length != 2 ||
			!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
			!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var second) ||
			first < 1 ||
			second < 1)
		{
			return false;
		}

		size = cells ? OfCells(first, second) : new PreviewSize(first, second);

		return size.Width <= PreviewMetrics.MaxPixels && size.Height <= PreviewMetrics.MaxPixels;
	}

	public override string ToString() => $"{Width}x{Height}";
}
