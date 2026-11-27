using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class ControlComponents
{
	private const double ToggleAspect = 1.75;

	private const double SegmentFaceInset = 0.08;

	private const double VerticalSegmentExtent = 0.14;

	public static readonly ComponentDefinition Toggle = new(Components.Toggle,
		"div",
		PaintToggle,
		1,
		(node, metrics) =>
		{
			var size = UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope) ?? 0;
			return metrics.Horizontal ? ToggleAspect * size : size;
		});

	public static readonly ComponentDefinition Segmented = new(Components.Segmented,
		"div",
		PaintSegmented,
		2,
		(node, metrics) => !metrics.Horizontal && Vertical(node)
			? node.Children.Count * VerticalSegmentExtent * metrics.Basis
			: 0);

	private static bool Vertical(UiNode node)
	{
		return node.String("direction") == "vertical";
	}

	private static void ValueTint(UiNode node, NodeScope scope)
	{
		var claims = Modifiers.Emits(node, "change") && !scope.IsDisabled;
		scope.Element.SetClass("widget-pressable", claims);
		if (claims)
		{
			scope.Part("tint", "div").SetClassName("widget-press-tint");
		}
	}

	private static void PaintToggle(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-toggle");
		NodeScope.SizeTo(element, scope.Box);
		var width = scope.Box.Width ?? scope.Basis;
		var height = scope.Box.Height ?? scope.Basis;
		var trackHeight = scope.Resolve("size") ?? Math.Min(height, width / ToggleAspect);
		var trackWidth = ToggleAspect * trackHeight;
		var on = node.Boolean("on") == true;
		var track = scope.Part("track", "div");
		track.SetClassName("widget-toggle-track");
		track.SetStyle("width", NodeScope.Px(trackWidth));
		track.SetStyle("height", NodeScope.Px(trackHeight));
		track.SetStyle("left", NodeScope.Px((width - trackWidth) / 2));
		track.SetStyle("top", NodeScope.Px((height - trackHeight) / 2));
		track.SetStyle("border-radius", NodeScope.Px(trackHeight / 2));
		track.SetStyle("background", on ? Layouts.LevelColor(node) : "var(--color-bg-tertiary)");
		var knob = scope.Part("knob", "div", track);
		var diameter = 0.8 * trackHeight;
		var inset = 0.1 * trackHeight;
		knob.SetClassName("widget-toggle-knob");
		knob.SetStyle("width", NodeScope.Px(diameter));
		knob.SetStyle("height", NodeScope.Px(diameter));
		knob.SetStyle("top", NodeScope.Px(inset));
		knob.SetStyle("left", NodeScope.Px(on ? trackWidth - inset - diameter : inset));
		ValueTint(node, scope);
	}

	private static void PaintSegmented(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-segmented");
		var vertical = Vertical(node);
		element.SetClass("widget-segmented-vertical", vertical);
		double? naturalHeight = vertical && scope.Box.Height is null
			? node.Children.Count * VerticalSegmentExtent * scope.Basis
			: null;
		NodeScope.SizeTo(element, naturalHeight is null ? scope.Box : new BoxSize(scope.Box.Width, naturalHeight));
		var width = scope.Box.Width ?? scope.Basis;
		var height = naturalHeight ?? scope.Box.Height ?? scope.Basis;
		var count = node.Children.Count;
		var segment = count > 0 ? (vertical ? height : width) / count : vertical ? height : width;
		var edge = vertical ? Math.Min(segment, width) : height;
		var inset = SegmentFaceInset * edge;
		var track = scope.Part("track", "div");
		track.SetClassName("widget-segmented-track");
		track.SetStyle("border-radius", NodeScope.Px(edge / 2));
		var selected = node.Number("selected");
		var face = scope.Part("face", "div");
		var shown = selected is { } index && index >= 0 && index < count;
		var offset = (shown ? selected!.Value : 0) * segment + inset;
		face.SetClassName("widget-segmented-face");
		face.SetStyle("display", shown ? null : "none");
		face.SetStyle("left", NodeScope.Px(vertical ? inset : offset));
		face.SetStyle("top", NodeScope.Px(vertical ? offset : inset));
		face.SetStyle("width", NodeScope.Px(Math.Max(0, (vertical ? width : segment) - 2 * inset)));
		face.SetStyle("height", NodeScope.Px(Math.Max(0, (vertical ? segment : height) - 2 * inset)));
		face.SetStyle("border-radius", NodeScope.Px(Math.Max(0, edge / 2 - inset)));
		face.SetStyle("background", Layouts.LevelColor(node));
		var content = scope.Part("content", "div");
		content.SetClassName("widget-segmented-content");
		var box = vertical ? new BoxSize(width, segment) : new BoxSize(segment, height);
		scope.SyncChildren(content, node.Children.Select(child => new ChildEntry(child, box, null)));
		var placed = content.Children.Where(child => child.NodeId is not null).ToList();
		for (var at = 0; at < placed.Count; at++)
		{
			placed[at].SetStyle("left", NodeScope.Px(vertical ? 0 : at * segment));
			placed[at].SetStyle("top", vertical ? NodeScope.Px(at * segment) : null);
		}

		ValueTint(node, scope);
	}
}
