using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class VideoStreamComponent
{
	public static readonly ComponentDefinition VideoStream = new(Components.VideoStream,
		"div",
		Paint,
		1,
		(node, metrics) => UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope) ?? 0);

	private static void Paint(UiNode _, NodeScope scope)
	{
		var element = scope.Element;
		var extent = scope.Resolve("size") ?? 0;
		var box = new BoxSize(scope.Box.Width ?? extent, scope.Box.Height ?? extent);
		element.SetClassName("widget-video-stream-host");
		NodeScope.SizeTo(element, box);
		var view = scope.Part("view", "div");
		view.SetClassName("widget-video-stream");
		view.SetAttribute("data-status", "none");
		var frame = scope.Part("frame", "div", view);
		frame.SetClassName("widget-video-stream-frame");
		frame.SetStyle("left", "0px");
		frame.SetStyle("top", "0px");
		frame.SetStyle("width", NodeScope.Px(box.Width));
		frame.SetStyle("height", NodeScope.Px(box.Height));
	}
}
