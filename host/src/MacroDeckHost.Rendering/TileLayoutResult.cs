namespace MacroDeckHost.Rendering;

public readonly record struct LayoutBox(double X, double Y, double Width, double Height);

public sealed record TextLayout(
	string Text,
	double NaturalWidth,
	double GivenWidth,
	bool TruncatedX,
	bool TruncatedY,
	double FontSize);

public sealed record NodeLayout(string Type, LayoutBox Box, IReadOnlyList<TextLayout> Texts);

public sealed record TileLayoutResult(IReadOnlyDictionary<string, NodeLayout> Nodes);

public sealed record TileRenderOutput
{
	public required byte[] Pixels { get; init; }

	public required int PixelWidth { get; init; }

	public required int PixelHeight { get; init; }

	public DateTimeOffset? NextChangeAt { get; init; }

	public required IReadOnlyList<string> MissingResources { get; init; }
}
