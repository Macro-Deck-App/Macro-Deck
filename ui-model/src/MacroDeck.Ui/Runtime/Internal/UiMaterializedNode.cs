using System.Text.Json;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// The runtime's mutable shadow of one immutable <see cref="UiNode" />: the node instance it currently
/// stands for, its place in the tree, and the property cells that produced its properties.
///
/// <para>
/// This shadow is what makes structural sharing possible. A changed cell knows its owner, an owner knows its
/// parent, so a flush rebuilds exactly the spine from the root down to each changed node and puts every
/// untouched sibling into the new child list <b>by reference</b>. Nothing outside that spine is
/// re-allocated, and nothing outside it is re-serialized - which is the property the whole design exists for.
/// </para>
/// </summary>
internal sealed class UiMaterializedNode
{
	internal UiMaterializedNode(
		UiElement element,
		UiNode node,
		UiChildRegion region,
		List<UiMaterializedNode> children,
		UiMaterializedNode? fallback,
		List<UiPropertyCell> cells)
	{
		Element = element;
		Node = node;
		Region = region;
		Children = children;
		Fallback = fallback;
		Cells = cells;
	}

	/// <summary>The authored element this node was materialized from, which is where its event handlers and its
	/// binding live. An event arrives naming a node id, and the id is only on the node - so the node is what has
	/// to know its way back to the element that can answer.</summary>
	internal UiElement Element { get; }

	/// <summary>The immutable node this shadow currently stands for. Replaced - never mutated - when a flush
	/// rebuilds the spine through it.</summary>
	internal UiNode Node { get; set; }

	/// <summary>The parent shadow, or <c>null</c> for the root. Set by the linking pass once the whole tree
	/// exists, since a child is materialized before its parent.</summary>
	internal UiMaterializedNode? Parent { get; set; }

	/// <summary>True when this node occupies its parent's fallback slot rather than a place in its child
	/// list.</summary>
	internal bool IsFallbackOfParent { get; set; }

	/// <summary>How this node's child list is composed out of authored slots - see
	/// <see cref="UiChildRegion" />. <see cref="Children" /> is this region flattened.</summary>
	internal UiChildRegion Region { get; }

	/// <summary>The child shadows, in render order. Refreshed from <see cref="Region" /> when a structural
	/// reconcile changes what one of its slots contributes.</summary>
	internal List<UiMaterializedNode> Children { get; }

	/// <summary>The shadow of this node's fallback subtree, if it declared one.</summary>
	internal UiMaterializedNode? Fallback { get; }

	/// <summary>The cells feeding this node's properties, in declaration order.</summary>
	internal List<UiPropertyCell> Cells { get; }

	/// <summary>True when this node lies on the spine the current flush has to rebuild.</summary>
	internal bool NeedsRebuild { get; set; }

	/// <summary>Property values this flush established, applied when the spine is rebuilt.</summary>
	internal Dictionary<string, JsonElement>? PendingSets { get; set; }

	/// <summary>Property keys this flush found absent, removed when the spine is rebuilt.</summary>
	internal List<string>? PendingRemovals { get; set; }

	/// <summary>Flags this node and its ancestors as part of the spine the current flush has to rebuild. Stops
	/// at an already-flagged ancestor, whose own ancestors are flagged by the same invariant.</summary>
	internal void MarkForRebuild()
	{
		for (var current = this; current is not null && !current.NeedsRebuild; current = current.Parent)
		{
			current.NeedsRebuild = true;
		}
	}
}
