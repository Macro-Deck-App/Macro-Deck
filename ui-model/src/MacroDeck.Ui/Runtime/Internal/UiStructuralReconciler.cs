using System.Text.Json;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// Turns an invalidated structural scope into the operations that carry its change: one
/// <c>remove-node</c> per removed subtree, <c>move-node</c> for a survivor that changed position,
/// <c>insert-node</c> for new content, <c>replace-node</c> for a survivor whose node type changed, and one
/// <c>set-properties</c> per survivor whose properties differ.
///
/// <para>
/// <b>Only the scope's own slot is rebuilt.</b> Everything outside the region the scope owns keeps its
/// materialized nodes and therefore its <see cref="UiNode" /> instances, so a conditional flipping deep in a
/// flow does not re-allocate or re-serialize the rest of the tree. Inside the slot, the whole subtree is
/// re-materialized and the operations are computed by diffing the old shadow against the new one, which is
/// what makes a survivor keep its id - and with it the focus and in-flight edits stable identity exists to
/// preserve.
/// </para>
///
/// <para>
/// <b>Indices are simulated, never authored.</b> Every index an operation carries is the index in the parent's
/// child list <i>as it will be when that operation applies</i>: the operations for one parent are emitted as
/// removes, then moves, then inserts, each evaluated against a running simulation of that child list. An index
/// the simulation says is out of range is an internal invariant failure and throws - clamping it would hide a
/// producer bug behind a patch the renderer silently mis-applies.
/// </para>
/// </summary>
internal sealed class UiStructuralReconciler
{
	private readonly UiView _view;

	internal UiStructuralReconciler(UiView view) => _view = view;

	/// <summary>Reconciles <paramref name="scope" /> and appends what it changed to
	/// <paramref name="operations" />. Appends nothing when the scope's decision is the one it already
	/// produced.</summary>
	internal void Reconcile(UiStructuralScope scope, List<UiPatchOperation> operations)
	{
		if (!TryEvaluateDecision(scope, out var content))
		{
			return;
		}

		var region = scope.Region;

		if (region.Owner is not { } owner)
		{
			throw new UiViewException($"The conditional or repeat keyed '{scope.Element.Key}' declared near " +
				$"'{scope.ParentDeclarationPath}' occupies a slot that must always hold exactly one node - the " +
				"tree root or a Fallback - so its content cannot change. Move it inside the element that " +
				"fills the slot.");
		}

		var oldNodes = new List<UiMaterializedNode>();
		region.Flatten(oldNodes);

		// Captured before the region is rewritten: these are the indices every operation for this parent is
		// measured against.
		var baseIndex = region.BaseIndex;
		var simulated = new List<string>(owner.Children.Count);
		foreach (var child in owner.Children)
		{
			simulated.Add(child.Node.Id);
		}

		region.ReleaseNestedScopes();
		foreach (var node in oldNodes)
		{
			ReleaseSubtree(node);
		}

		region.Clear();

		var materializer = new UiElementMaterializer(_view);
		foreach (var (segment, element) in content)
		{
			materializer.MaterializeInto(element,
				region,
				scope.StructuralPrefix,
				scope.InputScope,
				UiElementMaterializer.Extend(scope.ParentDeclarationPath, segment));
		}

		var newNodes = new List<UiMaterializedNode>();
		region.Flatten(newNodes);

		foreach (var node in newNodes)
		{
			UiElementMaterializer.Link(node, owner, isFallbackOfParent: false);
		}

		ReconcileChildLists(owner, oldNodes, newNodes, baseIndex, simulated, operations);

		owner.Children.Clear();
		owner.Region.Flatten(owner.Children);
		owner.MarkForRebuild();
	}

	/// <summary>
	/// Re-evaluates <paramref name="scope" />'s decision and reports whether it changed, handing back the
	/// content to materialize when it did.
	///
	/// <para>
	/// The decision is evaluated inside the scope but <b>without</b> dropping its dependencies first. An
	/// unchanged decision returns without invoking the content closure at all, so clearing would unsubscribe
	/// the states only that closure reads. Dependencies therefore accumulate; a state that is no longer read
	/// merely invalidates a scope that then finds nothing changed and emits nothing.
	/// </para>
	/// </summary>
	private static bool TryEvaluateDecision(
		UiStructuralScope scope,
		out List<(string Segment, UiElement Element)> content)
	{
		content = [];

		using var tracking = UiTracking.Push(scope);

		switch (scope.Element)
		{
			case UiWhen when1:
			{
				var condition = when1.Condition();

				if (condition == scope.LastCondition)
				{
					return false;
				}

				scope.LastCondition = condition;

				if (condition)
				{
					content.Add((when1.Key, when1.Content()));
				}

				return true;
			}

			case IUiRepeatElement repeat:
			{
				var expansion = repeat.Expand(key => UiElementMaterializer.Compose(scope.InputScope, key));

				// The list instance is the whole gate, deliberately not the key sequence. Replacing one item
				// with a new instance carrying the same key is how immutable item records are edited, and the
				// templates close over the item instances, so an unchanged key sequence says nothing about
				// whether the items still render the same thing. A replaced list re-runs the templates; what
				// keeps the resulting patch minimal is the id diff below, not this comparison.
				if (ReferenceEquals(expansion.ItemList, scope.LastItemList))
				{
					return false;
				}

				scope.LastItemList = expansion.ItemList;

				foreach (var item in expansion.Items)
				{
					content.Add((item.ItemKey, item.Build()));
				}

				return true;
			}

			default:
				return false;
		}
	}

	/// <summary>Releases every cell and every nested structural scope of a subtree that left the tree, so a
	/// later state write touches only what is still rendered.</summary>
	private static void ReleaseSubtree(UiMaterializedNode node)
	{
		foreach (var cell in node.Cells)
		{
			cell.Release();
		}

		node.Region.ReleaseNestedScopes();

		foreach (var child in node.Children)
		{
			ReleaseSubtree(child);
		}

		if (node.Fallback is not null)
		{
			ReleaseSubtree(node.Fallback);
		}
	}

	/// <summary>
	/// Diffs one run of <paramref name="parent" />'s child list by id and appends the operations that turn
	/// <paramref name="oldNodes" /> into <paramref name="newNodes" />.
	/// </summary>
	private static void ReconcileChildLists(
		UiMaterializedNode parent,
		List<UiMaterializedNode> oldNodes,
		List<UiMaterializedNode> newNodes,
		int baseIndex,
		List<string> simulated,
		List<UiPatchOperation> operations)
	{
		var oldById = new Dictionary<string, UiMaterializedNode>(StringComparer.Ordinal);
		foreach (var node in oldNodes)
		{
			oldById[node.Node.Id] = node;
		}

		var newIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var node in newNodes)
		{
			newIds.Add(node.Node.Id);
		}

		// Removals first, and only for the top-most node of each removed subtree: the model removes the
		// subtree along with the node, so a remove per descendant would name nodes that no longer exist.
		foreach (var node in oldNodes)
		{
			if (newIds.Contains(node.Node.Id))
			{
				continue;
			}

			operations.Add(new UiPatchOperation
			{
				Op = UiPatchOperations.RemoveNode,
				NodeId = node.Node.Id,
			});

			simulated.Remove(node.Node.Id);
		}

		// Then moves. Deliberately a single forward pass rather than a longest-increasing-subsequence
		// minimizer: nothing in the contract asks for a minimal number of moves, and a minimizer is an
		// algorithm a test could only check by reimplementing it - which would make the test agree with the
		// implementation instead of with the requirement. This pass can emit one move more than strictly
		// necessary; every move it emits is correct.
		var survivorOffset = 0;

		foreach (var node in newNodes)
		{
			var id = node.Node.Id;

			if (!oldById.ContainsKey(id))
			{
				continue;
			}

			var desired = baseIndex + survivorOffset;
			survivorOffset++;

			var current = simulated.IndexOf(id);

			if (current < 0)
			{
				throw new UiViewException(
					$"Internal invariant: the surviving node '{id}' is not in its parent's child list.");
			}

			if (current == desired)
			{
				continue;
			}

			simulated.RemoveAt(current);

			// The model's rule: a move's index is the index after the node left its old position.
			if (desired > simulated.Count)
			{
				throw new UiViewException(
					$"Internal invariant: move-node '{id}' computed index {desired}, beyond the " +
					$"{simulated.Count} children its parent has once the node is removed.");
			}

			simulated.Insert(desired, id);

			operations.Add(new UiPatchOperation
			{
				Op = UiPatchOperations.MoveNode,
				NodeId = id,
				ParentId = parent.Node.Id,
				Index = desired,
			});
		}

		// Then inserts. Every earlier position already holds its final node, so the new node's own position in
		// the new list is the index it applies at.
		for (var index = 0; index < newNodes.Count; index++)
		{
			var node = newNodes[index];
			var position = baseIndex + index;

			if (position < simulated.Count &&
				string.Equals(simulated[position], node.Node.Id, StringComparison.Ordinal))
			{
				continue;
			}

			if (position > simulated.Count)
			{
				throw new UiViewException(
					$"Internal invariant: insert-node '{node.Node.Id}' computed index {position}, beyond the " +
					$"{simulated.Count} children its parent has.");
			}

			operations.Add(new UiPatchOperation
			{
				Op = UiPatchOperations.InsertNode,
				NodeId = node.Node.Id,
				ParentId = parent.Node.Id,
				Index = position == simulated.Count ? null : position,
				Node = node.Node,
			});

			simulated.Insert(position, node.Node.Id);
		}

		foreach (var node in newNodes)
		{
			if (oldById.TryGetValue(node.Node.Id, out var previous))
			{
				ReconcileSurvivor(previous, node, operations);
			}
		}
	}

	/// <summary>Diffs a node that kept its id: a changed type or component version is a replacement, anything
	/// else is a property diff plus a recursion into the children.</summary>
	private static void ReconcileSurvivor(
		UiMaterializedNode previous,
		UiMaterializedNode current,
		List<UiPatchOperation> operations)
	{
		// A renderer cannot morph one component into another, and the fallback slot has no operation of its
		// own, so either difference is answered by replacing the whole node.
		if (!string.Equals(previous.Node.Type, current.Node.Type, StringComparison.Ordinal) ||
			previous.Node.RequiredComponentVersion != current.Node.RequiredComponentVersion ||
			!FallbacksAlign(previous.Node.Fallback, current.Node.Fallback))
		{
			operations.Add(new UiPatchOperation
			{
				Op = UiPatchOperations.ReplaceNode,
				NodeId = current.Node.Id,
				Node = current.Node,
			});

			return;
		}

		DiffProperties(previous.Node, current.Node, operations);

		if (previous.Fallback is not null && current.Fallback is not null)
		{
			ReconcileSurvivor(previous.Fallback, current.Fallback, operations);
		}

		var simulated = new List<string>(previous.Children.Count);
		foreach (var child in previous.Children)
		{
			simulated.Add(child.Node.Id);
		}

		ReconcileChildLists(current, previous.Children, current.Children, baseIndex: 0, simulated, operations);
	}

	private static bool FallbacksAlign(UiNode? previous, UiNode? current)
	{
		if (previous is null || current is null)
		{
			return previous is null && current is null;
		}

		return string.Equals(previous.Id, current.Id, StringComparison.Ordinal) &&
			string.Equals(previous.Type, current.Type, StringComparison.Ordinal) &&
			previous.RequiredComponentVersion == current.RequiredComponentVersion;
	}

	/// <summary>Emits one <c>set-properties</c> for the keys that actually differ. A key that is gone belongs
	/// in <c>RemovedProperties</c>, never in <c>Properties</c> with a JSON <c>null</c>, which the model defines
	/// as "explicitly null" instead.</summary>
	private static void DiffProperties(UiNode previous, UiNode current, List<UiPatchOperation> operations)
	{
		Dictionary<string, JsonElement>? sets = null;
		List<string>? removals = null;

		foreach (var (key, value) in current.Properties)
		{
			if (previous.Properties.TryGetValue(key, out var before) &&
				string.Equals(before.GetRawText(), value.GetRawText(), StringComparison.Ordinal))
			{
				continue;
			}

			(sets ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[key] = value;
		}

		foreach (var key in previous.Properties.Keys)
		{
			if (!current.Properties.ContainsKey(key))
			{
				(removals ??= []).Add(key);
			}
		}

		if (sets is null && removals is null)
		{
			return;
		}

		operations.Add(new UiPatchOperation
		{
			Op = UiPatchOperations.SetProperties,
			NodeId = current.Id,
			Properties = sets,
			RemovedProperties = removals,
		});
	}
}
