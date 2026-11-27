using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed record ListLayout(NodeScope Scope, double Padding, bool Horizontal, double? Given);

internal static class ListComponent
{
	public static readonly ComponentDefinition List = new(Components.List, "div", Paint, 3, (_, _) => 0);

	private static void Paint(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		var padding = Layouts.Length(node, "padding", scope.Scope);
		var gap = Layouts.Length(node, "gap", scope.Scope);
		var horizontal = Layouts.IsHorizontal(node);
		var anchored = !horizontal && node.String("anchor") == "end";
		element.SetClassName("widget-list");
		element.SetClass("widget-list-horizontal", horizontal);
		element.SetClass("widget-list-anchor-end", anchored);
		element.SetStyle("gap", NodeScope.Px(gap));
		element.SetStyle("padding", NodeScope.Px(padding));
		if (!Modifiers.Owns(node, scope, "background"))
		{
			element.SetStyle("background", UiLength.BackgroundColor(node, "background"));
		}

		element.SetStyle("width", NodeScope.Px(scope.Box.Width));
		element.SetStyle("height", NodeScope.Px(scope.Box.Height));
		var cross = horizontal ? scope.Box.Height : scope.Box.Width;
		double? given = cross is { } value ? Math.Max(0, value - 2 * padding) : null;
		LayOut(scope, horizontal, given);
		scope.Host.Lists.Add(element);
		element.Paint = new ListLayout(scope, padding, horizontal, given);
		if (anchored)
		{
			scope.Host.AnchoredLists.Add(element);
		}
	}

	public static void LayOut(NodeScope scope, bool horizontal, double? inner)
	{
		var element = scope.Element;
		foreach (var child in element.Children.Where(child => child.NodeId is not null).ToList())
		{
			element.Children.Remove(child);
			scope.Host.Forget(child);
		}

		scope.SyncChildren(element,
			scope.Node.Children.Select(child =>
				new ChildEntry(child, horizontal ? new BoxSize(null, inner) : new BoxSize(inner, null), inner)));
	}

	public static bool Relayout(Element element, int clientCross)
	{
		if (element.Paint is not ListLayout layout || clientCross == 0)
		{
			return false;
		}

		var available = Math.Max(0, clientCross - 2 * layout.Padding);
		if (layout.Given is { } given && Math.Abs(available - given) < 0.5)
		{
			return false;
		}

		LayOut(layout.Scope, layout.Horizontal, available);
		element.Paint = layout with { Given = available };
		return true;
	}
}
