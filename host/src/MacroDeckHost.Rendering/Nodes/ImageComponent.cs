using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class ImageComponent
{
	public static readonly ComponentDefinition Image = new(Components.Image,
		"div",
		Paint,
		1,
		(node, metrics) => UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope) ?? 0);

	private static void Paint(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-image");
		element.SetClass("widget-artwork-eased", node.String("transition") == "crossfade");
		NodeScope.SizeTo(element, scope.Box);
		var edge = scope.Resolve("size");
		var source = scope.Host.Resource(node.Raw("source"), edge is > 0 ? edge * Layouts.Zoom(node) : null);
		if (source is null)
		{
			return;
		}

		var image = scope.Part("image", "img");
		image.SetStyle("width", NodeScope.Px(edge));
		image.SetStyle("height", NodeScope.Px(edge));
		image.SetStyle("transform", Layouts.ArtworkTransform(node));
		image.SetStyle("filter", Layouts.ArtworkFilter(node));
		image.SetStyle("opacity", NodeScope.Num(Layouts.Opacity(node)));
		image.SetAttribute("data-tint", UiLength.HexColor(node, "tint"));
		image.Paint = source;
	}
}
