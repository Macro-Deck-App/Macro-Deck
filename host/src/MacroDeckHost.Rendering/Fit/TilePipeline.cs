using System.Text.Json;
using MacroDeckHost.Rendering.Nodes;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Layout;
using MacroDeckHost.Rendering.Text;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Fit;

internal sealed class TilePipeline
{
	private const int FitPasses = 2;

	private const double FitEpsilon = 0.05;

	private const int FitSteps = 3;

	private const int NudgeSteps = 4;

	private const double NudgeFactor = 0.995;

	private readonly StyleResolver _styles;

	private readonly LayoutEngine _layout;

	private TilePipeline(TileRenderInput input, TextShaper shaper, IReadOnlyDictionary<string, int>? versionCaps)
	{
		Input = input;
		Shaper = shaper;
		Host = new RenderHost(input, versionCaps);
		_styles = new StyleResolver(StyleSheet.Shared,
			input.Theme.Tokens(),
			style => shaper.ZeroWidth(InlineLayout.FontOf(style)));
		_layout = new LayoutEngine(new InlineLayout(shaper));
		Document = new Element("html");
		Document.SetStyle("--font-sans", "MacroDeckSans");
		var body = new Element("body");
		Document.Append(body);
		Tile = new Element("div");
		Tile.SetStyle("position", "relative");
		Tile.SetStyle("overflow", "hidden");
		Tile.SetStyle("box-sizing", "border-box");
		Tile.SetStyle("background", input.TransparentTile ? "transparent" : "var(--color-bg-secondary)");
		Tile.SetStyle("color", "var(--color-text-primary)");
		Tile.SetStyle("width", NodeScope.Px(input.Width));
		Tile.SetStyle("height", NodeScope.Px(input.Height));
		body.Append(Tile);
		Surface = new Element("div");
		Surface.SetStyle("width", NodeScope.Px(input.Width));
		Surface.SetStyle("height", NodeScope.Px(input.Height));
		Surface.SetAttribute("dir", "ltr");
		Tile.Append(Surface);
	}

	public TileRenderInput Input { get; }

	public TextShaper Shaper { get; }

	public RenderHost Host { get; }

	public Element Document { get; }

	public Element Tile { get; }

	public Element Surface { get; }

	public UiNode? Root { get; private set; }

	public TreeBuilder? Builder { get; private set; }

	public LayoutEngine Engine => _layout;

	public static TilePipeline Run(TileRenderInput input, IRenderFonts fonts)
	{
		var pipeline = new TilePipeline(input, new TextShaper(fonts, input.Culture), null);
		pipeline.Build();
		pipeline.Measure();
		return pipeline;
	}

	public static TilePipeline Resolve(
		TileRenderInput input,
		IRenderFonts fonts,
		IReadOnlyDictionary<string, int>? versionCaps)
	{
		var pipeline = new TilePipeline(input, new TextShaper(fonts, input.Culture), versionCaps);
		pipeline.Build();
		return pipeline;
	}

	private void Build()
	{
		if (Input.Tree is { ValueKind: JsonValueKind.Object } tree && tree.TryGetProperty("root", out var rootValue))
		{
			Root = UiNode.Parse(rootValue);
		}

		if (Root is not null)
		{
			Builder = new TreeBuilder(Host);
			Builder.RenderRoot(Surface, Root, new BoxSize(Input.Width, Input.Height), Input.Basis);
		}

		Relayout();
	}

	private void Measure()
	{
		RelayoutLists();
		SettleFits();
		ScrollAnchoredLists();
	}

	public void Relayout()
	{
		_styles.ResolveTree(Document);
		var width = LayoutUnit.FromDouble(Input.Width);
		_layout.Layout(Document,
			new Constraint(width,
				width,
				LayoutUnit.FromDouble(Input.Height),
				width,
				LayoutUnit.FromDouble(Input.Height),
				false));
	}

	private void RelayoutLists()
	{
		var changed = false;
		foreach (var list in Host.Lists.ToList())
		{
			var horizontal = list.Classes.Contains("widget-list-horizontal");
			var (x, y) = Geometry.LayoutOffset(list);
			var client = horizontal
				? LayoutUnit.SnapSizeToPixel(list.Box.Height - list.Box.ScrollbarBottom, y)
				: LayoutUnit.SnapSizeToPixel(list.Box.Width - list.Box.ScrollbarRight, x);
			changed |= ListComponent.Relayout(list, client);
		}

		if (changed)
		{
			Relayout();
		}
	}

	private void ScrollAnchoredLists()
	{
		foreach (var list in Host.AnchoredLists)
		{
			var client = list.Box.Height - list.Box.ScrollbarBottom;
			list.Box.ScrollTop = Math.Max(0, (list.Box.ScrollableHeight - client).Value);
		}
	}

	// text-fit.ts runSettle: every fit back at its declared size, all measured, then all applied.
	private void SettleFits()
	{
		var fits = Host.Fits;
		if (fits.Count == 0)
		{
			return;
		}

		var previous = fits.Select(_ => (double?)null).ToList();
		for (var pass = 0; pass < FitPasses; pass++)
		{
			foreach (var fit in fits)
			{
				fit.Element.SetStyle("font-size", NodeScope.Px(fit.Declared));
			}

			Relayout();
			var widths = fits.Select(fit => fit.Element.Box.Width.Value).ToList();
			var sizes = new List<double?>();
			for (var index = 0; index < fits.Count; index++)
			{
				sizes.Add(Apply(fits[index], widths[index]));
			}

			Relayout();
			var moved = false;
			for (var index = 0; index < fits.Count; index++)
			{
				var before = previous[index];
				var size = sizes[index];
				if (size is null || before is null ? size != before : Math.Abs(size.Value - before.Value) > FitEpsilon)
				{
					moved = true;
				}
			}

			if (!moved)
			{
				return;
			}

			previous = sizes;
		}
	}

	private double? Apply(TextFitEntry fit, double available)
	{
		var size = fit.Declared;
		if (size is { } declared && fit.Minimum is { } minimum && available > 0)
		{
			var current = declared;
			for (var step = 0; step < FitSteps; step++)
			{
				var natural = NaturalWidth(fit.Element, current);
				if (natural <= available)
				{
					break;
				}

				var next = Math.Max(minimum, current * available / natural);
				if (next >= current)
				{
					break;
				}

				current = next;
			}

			for (var step = 0; step < NudgeSteps; step++)
			{
				var natural = NaturalWidth(fit.Element, current);
				if (natural <= available)
				{
					break;
				}

				var next = Math.Max(minimum, current * available / natural * NudgeFactor);
				if (next >= current)
				{
					break;
				}

				current = next;
			}

			size = current;
		}

		fit.Element.SetStyle("font-size", NodeScope.Px(size));
		return size;
	}

	public double NaturalWidth(Element element, double? fontSize)
	{
		var saved = element.Style.GetValueOrDefault("font-size");
		if (fontSize is { } size)
		{
			element.SetStyle("font-size", NodeScope.Px(size));
		}

		_styles.ResolveTree(Document);
		var style = element.Computed;
		var padding = LayoutEngine.Padding(style, null);
		var border = LayoutEngine.Border(style);
		var width = _layout.Inline.MaxContent(element) + padding.Horizontal + border.Horizontal;
		element.SetStyle("font-size", saved);
		_styles.ResolveTree(Document);
		return width.Value;
	}
}
