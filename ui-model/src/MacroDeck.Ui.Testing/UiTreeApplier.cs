using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;

namespace MacroDeck.Ui.Testing;

/// <summary>
/// The reference implementation of the four tree-dependent patch rules
/// <see cref="Model.Patches.UiPatchOperation" />'s remarks document but deliberately leave without an API in
/// <c>MacroDeck.Ui.Model</c>: index bounds, root-targeting operations, apply order, and atomicity. This is
/// the independent oracle a differ's output is folded through in later stages, so it favours being obviously
/// correct over being clever - a plain recursive tree rebuild, never a mutation of the input.
///
/// <para>
/// Context-free rules - a known <see cref="UiPatchOperations.Op" /> name, a valid node id, the fields each
/// operation requires - are delegated to <see cref="UiPatchOperationValidation.Validate" />, and the
/// revision gate to <see cref="UiPatchSequencing.CheckRevisions" />. Both are checked for every operation
/// before any operation is applied, so a patch that fails either never touches the tree at all.
/// </para>
/// </summary>
public static class UiTreeApplier
{
	/// <summary>
	/// Applies <paramref name="patch" /> to <paramref name="tree" />. Every operation must apply for the
	/// patch to apply: an inapplicable operation anywhere in the sequence discards the whole patch and
	/// returns the unchanged, reference-equal input tree. Operations apply in array order, each observing
	/// the previous one's effect - so an operation may target a node an earlier operation in the same patch
	/// created, moved or renamed. Never throws for a malformed or inapplicable patch; only a null
	/// <paramref name="tree" /> or <paramref name="patch" /> throws.
	/// </summary>
	public static UiApplyResult Apply(UiTree tree, UiPatch patch)
	{
		ArgumentNullException.ThrowIfNull(tree);
		ArgumentNullException.ThrowIfNull(patch);

		var sequencing = UiPatchSequencing.CheckRevisions(tree.Revision, patch);
		if (!sequencing.IsApplicable)
		{
			return Reject(tree, sequencing.RejectionReason!);
		}

		foreach (var operation in patch.Operations)
		{
			var validation = UiPatchOperationValidation.Validate(operation);
			if (!validation.IsApplicable)
			{
				return Reject(tree, validation.RejectionReason!);
			}
		}

		var root = tree.Root;

		foreach (var operation in patch.Operations)
		{
			var (nextRoot, reason) = ApplyOperation(root, operation);
			if (nextRoot is null)
			{
				return Reject(tree, reason!);
			}

			root = nextRoot;
		}

		return new UiApplyResult { IsApplied = true, Tree = tree with { Revision = patch.ToRevision, Root = root } };
	}

	private static UiApplyResult Reject(UiTree tree, string reason)
		=> new() { IsApplied = false, RejectionReason = reason, Tree = tree };

	private static (UiNode? Root, string? Reason) ApplyOperation(UiNode root, UiPatchOperation operation)
		=> operation.Op switch
		{
			UiPatchOperations.SetProperties => ApplySetProperties(root, operation),
			UiPatchOperations.InsertNode => ApplyInsertNode(root, operation),
			UiPatchOperations.RemoveNode => ApplyRemoveNode(root, operation),
			UiPatchOperations.ReplaceNode => ApplyReplaceNode(root, operation),
			UiPatchOperations.MoveNode => ApplyMoveNode(root, operation),
			_ => (null, $"Unknown patch operation \"{operation.Op}\"."),
		};

	private static (UiNode? Root, string? Reason) ApplySetProperties(UiNode root, UiPatchOperation operation)
	{
		var target = FindNode(root, operation.NodeId);
		if (target is null)
		{
			return (null, $"No node with id \"{operation.NodeId}\" exists.");
		}

		var properties = new Dictionary<string, JsonElement>(target.Properties, StringComparer.Ordinal);

		if (operation.Properties is not null)
		{
			foreach (var (key, value) in operation.Properties)
			{
				properties[key] = value;
			}
		}

		if (operation.RemovedProperties is not null)
		{
			foreach (var key in operation.RemovedProperties)
			{
				properties.Remove(key);
			}
		}

		var updated = target with { Properties = properties };
		TryReplace(root, operation.NodeId, updated, out var newRoot);
		return (newRoot, null);
	}

	private static (UiNode? Root, string? Reason) ApplyInsertNode(UiNode root, UiPatchOperation operation)
	{
		var parent = FindNode(root, operation.ParentId!);
		if (parent is null)
		{
			return (null, $"No node with id \"{operation.ParentId}\" exists to insert under.");
		}

		var count = parent.Children.Count;
		var index = operation.Index ?? count;
		if (index < 0 || index > count)
		{
			return (null,
				$"Index {index} is out of bounds for parent \"{operation.ParentId}\" with {count} children.");
		}

		var duplicateId = FindDuplicateId(root, excludedId: null, operation.Node!);
		if (duplicateId is not null)
		{
			return (null, $"\"{duplicateId}\" is already used by another node in the tree.");
		}

		var children = parent.Children.ToList();
		children.Insert(index, operation.Node!);
		var updatedParent = parent with { Children = children };

		TryReplace(root, parent.Id, updatedParent, out var newRoot);
		return (newRoot, null);
	}

	private static (UiNode? Root, string? Reason) ApplyRemoveNode(UiNode root, UiPatchOperation operation)
	{
		if (string.Equals(operation.NodeId, root.Id, StringComparison.Ordinal))
		{
			return (null, "remove-node may not target the tree's root.");
		}

		if (!TryRemoveChild(root, operation.NodeId, out var newRoot))
		{
			return (null, $"No node with id \"{operation.NodeId}\" exists.");
		}

		return (newRoot, null);
	}

	private static (UiNode? Root, string? Reason) ApplyReplaceNode(UiNode root, UiPatchOperation operation)
	{
		var replacement = operation.Node!;

		if (string.Equals(operation.NodeId, root.Id, StringComparison.Ordinal) &&
			!string.Equals(replacement.Id, root.Id, StringComparison.Ordinal))
		{
			return (null, "replace-node on the root may not change the root's id.");
		}

		if (FindNode(root, operation.NodeId) is null)
		{
			return (null, $"No node with id \"{operation.NodeId}\" exists.");
		}

		var duplicateId = FindDuplicateId(root, operation.NodeId, replacement);
		if (duplicateId is not null)
		{
			return (null, $"\"{duplicateId}\" is already used by another node in the tree.");
		}

		TryReplace(root, operation.NodeId, replacement, out var newRoot);
		return (newRoot, null);
	}

	private static (UiNode? Root, string? Reason) ApplyMoveNode(UiNode root, UiPatchOperation operation)
	{
		if (string.Equals(operation.NodeId, root.Id, StringComparison.Ordinal))
		{
			return (null, "move-node may not target the tree's root.");
		}

		var node = FindNode(root, operation.NodeId);
		if (node is null)
		{
			return (null, $"No node with id \"{operation.NodeId}\" exists.");
		}

		if (!TryRemoveChild(root, operation.NodeId, out var removedRoot))
		{
			return (null, $"No node with id \"{operation.NodeId}\" exists.");
		}

		var parent = FindNode(removedRoot, operation.ParentId!);
		if (parent is null)
		{
			return (null, $"No node with id \"{operation.ParentId}\" exists to move under.");
		}

		var count = parent.Children.Count;
		var index = operation.Index ?? count;
		if (index < 0 || index > count)
		{
			return (null,
				$"Index {index} is out of bounds for parent \"{operation.ParentId}\" with {count} children.");
		}

		var children = parent.Children.ToList();
		children.Insert(index, node);
		var updatedParent = parent with { Children = children };

		TryReplace(removedRoot, parent.Id, updatedParent, out var newRoot);
		return (newRoot, null);
	}

	/// <summary>Finds the node with <paramref name="id" /> anywhere in the subtree rooted at
	/// <paramref name="node" />, including inside <see cref="UiNode.Fallback" /> subtrees.</summary>
	private static UiNode? FindNode(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			var found = FindNode(child, id);
			if (found is not null)
			{
				return found;
			}
		}

		return node.Fallback is null ? null : FindNode(node.Fallback, id);
	}

	/// <summary>Rebuilds the spine from <paramref name="node" /> to the node with <paramref name="targetId" />,
	/// replacing it with <paramref name="replacement" /> and sharing every untouched sibling subtree.
	/// Returns <c>false</c> without touching <paramref name="result" />'s identity when the target is not
	/// found.</summary>
	private static bool TryReplace(UiNode node, string targetId, UiNode replacement, out UiNode result)
	{
		if (string.Equals(node.Id, targetId, StringComparison.Ordinal))
		{
			result = replacement;
			return true;
		}

		for (var i = 0; i < node.Children.Count; i++)
		{
			if (TryReplace(node.Children[i], targetId, replacement, out var updatedChild))
			{
				var children = node.Children.ToList();
				children[i] = updatedChild;
				result = node with { Children = children };
				return true;
			}
		}

		if (node.Fallback is not null && TryReplace(node.Fallback, targetId, replacement, out var updatedFallback))
		{
			result = node with { Fallback = updatedFallback };
			return true;
		}

		result = node;
		return false;
	}

	/// <summary>Rebuilds the spine from <paramref name="node" /> to the parent whose <c>Children</c> list
	/// directly contains a node with <paramref name="targetId" />, removing it. Searches inside
	/// <see cref="UiNode.Fallback" /> subtrees as well.</summary>
	private static bool TryRemoveChild(UiNode node, string targetId, out UiNode result)
	{
		var index = -1;
		for (var i = 0; i < node.Children.Count; i++)
		{
			if (string.Equals(node.Children[i].Id, targetId, StringComparison.Ordinal))
			{
				index = i;
				break;
			}
		}

		if (index >= 0)
		{
			var children = node.Children.ToList();
			children.RemoveAt(index);
			result = node with { Children = children };
			return true;
		}

		for (var i = 0; i < node.Children.Count; i++)
		{
			if (TryRemoveChild(node.Children[i], targetId, out var updatedChild))
			{
				var children = node.Children.ToList();
				children[i] = updatedChild;
				result = node with { Children = children };
				return true;
			}
		}

		if (node.Fallback is not null && TryRemoveChild(node.Fallback, targetId, out var updatedFallback))
		{
			result = node with { Fallback = updatedFallback };
			return true;
		}

		result = node;
		return false;
	}

	/// <summary>Returns the first id in <paramref name="newSubtree" /> (including its own id, its
	/// descendants and any <see cref="UiNode.Fallback" /> subtrees) that already exists somewhere in
	/// <paramref name="root" />, other than inside the subtree rooted at <paramref name="excludedId" /> -
	/// the node a replace-node is removing. <c>null</c> when no such id exists.</summary>
	private static string? FindDuplicateId(UiNode root, string? excludedId, UiNode newSubtree)
	{
		var existingIds = new HashSet<string>(StringComparer.Ordinal);
		CollectIds(root, excludedId, existingIds);

		foreach (var id in SubtreeIds(newSubtree))
		{
			if (existingIds.Contains(id))
			{
				return id;
			}
		}

		return null;
	}

	private static void CollectIds(UiNode node, string? excludedId, HashSet<string> ids)
	{
		if (excludedId is not null && string.Equals(node.Id, excludedId, StringComparison.Ordinal))
		{
			return;
		}

		ids.Add(node.Id);

		foreach (var child in node.Children)
		{
			CollectIds(child, excludedId, ids);
		}

		if (node.Fallback is not null)
		{
			CollectIds(node.Fallback, excludedId, ids);
		}
	}

	private static IEnumerable<string> SubtreeIds(UiNode node)
	{
		yield return node.Id;

		foreach (var child in node.Children)
		{
			foreach (var id in SubtreeIds(child))
			{
				yield return id;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var id in SubtreeIds(node.Fallback))
			{
				yield return id;
			}
		}
	}
}
