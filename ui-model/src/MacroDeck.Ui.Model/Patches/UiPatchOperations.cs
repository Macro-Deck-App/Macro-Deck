namespace MacroDeck.Ui.Model.Patches;

/// <summary>
/// The five frozen operation names a <see cref="UiPatchOperation.Op" /> can carry. Not an enum: an
/// enum-backed converter throws on an unrecognised value, which would turn "unknown operation" into a
/// deserializer exception - exactly the behaviour this model forbids. Case-sensitive; comparisons use
/// <see cref="StringComparer.Ordinal" />.
/// </summary>
public static class UiPatchOperations
{
	/// <summary>Sets or removes properties on an existing node.</summary>
	public const string SetProperties = "set-properties";

	/// <summary>Inserts a new node into a parent's child list.</summary>
	public const string InsertNode = "insert-node";

	/// <summary>Removes a node from its parent.</summary>
	public const string RemoveNode = "remove-node";

	/// <summary>Replaces a node with another, keeping the same position.</summary>
	public const string ReplaceNode = "replace-node";

	/// <summary>Moves an existing node to a new position, possibly under a new parent - without it, a
	/// list reorder is N replaces, which throws away exactly the focus and in-flight edits stable
	/// identity exists to preserve.</summary>
	public const string MoveNode = "move-node";

	/// <summary>The five operation names, in the order above.</summary>
	public static readonly IReadOnlyList<string> All =
		[SetProperties, InsertNode, RemoveNode, ReplaceNode, MoveNode];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	/// <summary>True when <paramref name="op" /> is exactly one of the five frozen names. Never throws,
	/// including on <c>null</c>.</summary>
	public static bool IsKnown(string? op) => op is not null && _known.Contains(op);
}
