using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class StackComponents
{
	private const double ButtonCornerRatio = 0.12;

	private static readonly string[] _borderStyles =
		["static", "heartbeat", "breathing", "blink", "comet", "ants", "hue-shift", "rgb"];

	public static readonly ComponentDefinition Stack = new(Components.Stack,
		"div",
		(node, scope) => PaintStack(node, scope, false),
		2,
		StackIntrinsic);

	public static readonly ComponentDefinition Button = new(Components.Button, "div", PaintButton);

	private static double StackIntrinsic(UiNode node, IntrinsicMetrics metrics)
	{
		if (node.Children.Count == 0)
		{
			return 0;
		}

		var horizontal = Layouts.IsHorizontal(node);
		var padding = Layouts.Length(node, "padding", metrics.Scope);
		var gap = Layouts.Length(node, "gap", metrics.Scope);
		var nested = metrics.Nested();
		var extents = node.Children.Select(nested.OfChild).ToList();
		var inner = horizontal == metrics.Horizontal
			? extents.Sum() + Math.Max(0, node.Children.Count - 1) * gap
			: extents.Max();
		return inner + 2 * padding;
	}

	private static void PaintButton(UiNode node, NodeScope scope)
	{
		PaintStack(node, scope, true);
		PaintArtwork(node, scope);
	}

	private static void PaintStack(UiNode node, NodeScope scope, bool isButton)
	{
		var element = scope.Element;
		var horizontal = Layouts.IsHorizontal(node);
		var padding = Layouts.Length(node, "padding", scope.Scope);
		var gap = Layouts.Length(node, "gap", scope.Scope);
		element.SetClassName(isButton ? "widget-button" : "widget-stack");
		element.SetClass("widget-artwork-eased", node.String("transition") == "crossfade");
		var tileCorner = isButton && !scope.IsTreeRoot && node.String("corner") == "tile";
		element.SetClass("widget-node-root", isButton && scope.IsTreeRoot);
		element.SetClass("widget-tile-corner", tileCorner);
		if (!Modifiers.Owns(node, scope, "background"))
		{
			var background = UiLength.BackgroundColor(node, "background");
			element.SetStyle("background", isButton ? background ?? "var(--color-accent)" : background);
		}

		if (!Modifiers.Owns(node, scope, "border-radius"))
		{
			element.SetStyle("border-radius",
				isButton && !scope.IsTreeRoot && !tileCorner && scope.Box.Height is > 0
					? NodeScope.Px(scope.Box.Height * ButtonCornerRatio)
					: null);
		}

		element.SetStyle("flex-direction", horizontal ? "row" : "column");
		var clipStart = !isButton && node.String("overflow") == "clip-start";
		element.SetClass("widget-stack-clip-start", clipStart);
		element.SetStyle("justify-content", clipStart ? "flex-end" : Layouts.Justify(node));
		element.SetStyle("align-items", Layouts.Align(node));
		element.SetStyle("gap", NodeScope.Px(gap));
		element.SetStyle("padding", NodeScope.Px(padding));
		NodeScope.SizeTo(element, scope.Box);
		scope.SyncChildren(element,
			Layouts.StackChildren(node, scope.Box, scope.Basis, padding, gap, horizontal, clipStart));
		scope.PressTint();
	}

	private static void PaintArtwork(UiNode node, NodeScope scope)
	{
		var edge = Math.Max(scope.Box.Width ?? 0, scope.Box.Height ?? 0);
		var artwork = scope.Host.Resource(node.Raw("source"), edge > 0 ? edge * Layouts.Zoom(node) : null);
		if (artwork is not null)
		{
			var image = scope.Part("artwork", "img");
			image.SetClassName("widget-button-artwork");
			image.SetStyle("object-fit", Layouts.Fit(node));
			image.SetStyle("transform", Layouts.ArtworkTransform(node));
			image.SetStyle("filter", Layouts.ArtworkFilter(node));
			image.SetStyle("opacity", NodeScope.Num(Layouts.Opacity(node)));
			image.SetAttribute("data-tint", UiLength.HexColor(node, "tint"));
			image.Paint = artwork;
		}

		var style = node.String("borderStyle");
		var ring = scope.IsTreeRoot && scope.Host.OwnsRootBorder || style is null || !_borderStyles.Contains(style)
			? null
			: style;
		if (ring is not null)
		{
			var overlay = scope.Part("ring", "div");
			overlay.SetClassName("widget-button-ring");
			overlay.SetAttribute("data-ring-style", ring);
			overlay.SetAttribute("data-ring-color", UiLength.HexColor(node, "borderColor"));
			scope.Host.HasRing = true;
		}
	}
}
