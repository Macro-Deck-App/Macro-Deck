using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed class TreeBuilder
{
	private readonly RenderHost _host;

	public TreeBuilder(RenderHost host)
	{
		_host = host;
	}

	public List<NodeScope> Scopes { get; } = [];

	public void RenderRoot(Element container, UiNode node, BoxSize box, double basis)
	{
		Render(container, node, box, null, basis, box.Extent, false, false, true);
	}

	public void Render(
		Element container,
		UiNode next,
		BoxSize? box,
		double? crossExtent,
		double basis,
		double? containerExtent,
		bool parentDisabled,
		bool parentInsideButton,
		bool? inheritedTreeRoot)
	{
		var (current, unsupported) = Components.Resolve(next, _host.VersionCaps);
		var definition = unsupported is null ? Components.Get(current.Type) : null;
		var type = unsupported ?? current.Type;
		var element = new Element(definition?.Tag ?? "div") { Node = current };
		container.Append(element);
		element.SetAttribute("data-node-id", current.Id);
		element.SetAttribute("data-node-type", type);
		var isTreeRoot = inheritedTreeRoot ?? container.Closest(candidate => candidate.NodeId is not null) is null;
		var resolved = box ?? new BoxSize(basis, basis);
		var scope = new NodeScope
		{
			Builder = this,
			Host = _host,
			Element = element,
			Node = current,
			Basis = basis,
			Container = containerExtent,
			CrossExtent = crossExtent,
			Box = resolved,
			GivenBox = box,
			IsTreeRoot = isTreeRoot,
			ParentDisabled = parentDisabled,
			ParentInsideButton = parentInsideButton,
			TransparentRoot = definition?.TransparentRoot == true
		};
		Scopes.Add(scope);
		var plan = Modifiers.Plan(current, scope, definition?.ModifierInputs?.Invoke(current));
		if (definition is not null)
		{
			definition.Paint(current, scope);
			if (definition.TickPeriodMs?.Invoke(current) is { } period)
			{
				_host.TickPeriodsMs.Add(period);
			}
		}
		else
		{
			element.SetClassName("widget-node-unsupported");
			element.SetAttribute("data-unsupported-type", type);
			NodeScope.SizeTo(element, resolved);
		}

		Modifiers.Apply(plan, scope);
	}
}
