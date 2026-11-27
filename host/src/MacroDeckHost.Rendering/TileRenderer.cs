using MacroDeckHost.Rendering.Animation;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Fit;
using MacroDeckHost.Rendering.Layout;
using MacroDeckHost.Rendering.Paint;

namespace MacroDeckHost.Rendering;

public sealed class TileRenderer
{
	private readonly IRenderFonts _fonts;

	public TileRenderer(IRenderFonts? fonts = null)
	{
		_fonts = fonts ?? new RenderFonts(null);
	}

	public TileLayoutResult Layout(TileRenderInput input)
	{
		ArgumentNullException.ThrowIfNull(input);
		var pipeline = TilePipeline.Run(input, input.Fonts ?? _fonts);
		var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);
		foreach (var element in pipeline.Surface.Descendants())
		{
			if (element.NodeId is not { } id || !element.Box.Displayed)
			{
				continue;
			}

			var rect = Geometry.BoundingRect(element, pipeline.Surface);
			var texts = TextsOf(element).Select(text => Measure(pipeline, text)).ToList();
			nodes[id] = new NodeLayout(element.Attributes["data-node-type"],
				new LayoutBox(rect.X, rect.Y, rect.Width, rect.Height),
				texts);
		}

		return new TileLayoutResult(nodes);
	}

	public TileRenderOutput Render(TileRenderInput input)
	{
		ArgumentNullException.ThrowIfNull(input);
		var pipeline = TilePipeline.Run(input, input.Fonts ?? _fonts);
		var (pixels, width, height) = Painter.Paint(pipeline);
		return new TileRenderOutput
		{
			Pixels = pixels,
			PixelWidth = width,
			PixelHeight = height,
			NextChangeAt = NextChange.Of(pipeline),
			MissingResources = pipeline.Host.MissingResources.Order(StringComparer.Ordinal).ToList()
		};
	}

	private static IEnumerable<Element> TextsOf(Element node)
	{
		return new[] { node }.Concat(node.Descendants())
			.Where(element => !element.IsText
				&& element.Classes.Contains("widget-text")
				&& element.Closest(candidate => candidate.NodeId is not null) == node);
	}

	private static TextLayout Measure(TilePipeline pipeline, Element text)
	{
		var box = text.Box;
		var style = text.Computed;
		var border = LayoutEngine.Border(style);
		var (x, _) = Geometry.LayoutOffset(text);
		var clientLeft = x + border.Left;
		var client = LayoutUnit.SnapSizeToPixel(box.Width - border.Horizontal - box.ScrollbarRight, clientLeft);
		var scroll = LayoutUnit.SnapSizeToPixel(box.ScrollableWidth, clientLeft);
		var natural = pipeline.NaturalWidth(text, null);
		var unclamped = UnclampedHeight(pipeline, text);
		return new TextLayout(text.TextContent(),
			natural,
			box.Width.Value,
			scroll > client,
			unclamped > box.Height.Value + 0.5,
			style.FontSize);
	}

	private static double UnclampedHeight(TilePipeline pipeline, Element text)
	{
		var style = text.Computed;
		var padding = LayoutEngine.Padding(style, null);
		var border = LayoutEngine.Border(style);
		var content = (text.Box.Width - padding.Horizontal - border.Horizontal).ClampNegativeToZero();
		var lines = pipeline.Engine.Inline.BuildLines(text, content).Count;
		var (ascent, descent) = pipeline.Engine.LineMetrics(text);
		var height = (ascent + descent) * lines + padding.Vertical + border.Vertical;
		return height.Value;
	}
}
