using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Ui.Model.Patches;

/// <summary>
/// One operation of a <see cref="UiPatch" />. One record carries every field any of the five
/// <see cref="UiPatchOperations" /> can set, and the applier picks which fields matter by
/// <see cref="Op" /> - not <c>[JsonDerivedType]</c>, which throws on an unrecognised discriminator, the
/// same trap an enum-backed type would set for an unknown value.
///
/// <para>
/// Every rule below is frozen and specified here because it was found to be a coin-flip between two
/// independent renderers otherwise:
/// </para>
///
/// <list type="number">
/// <item>A key present in both <see cref="Properties" /> and <see cref="RemovedProperties" /> rejects
/// the operation - a producer bug, never a merge policy.</item>
/// <item><see cref="UiPatchOperations.MoveNode" />'s <see cref="Index" /> is the index in the parent's
/// child list <b>after</b> the node is removed from its old position; a <c>null</c> value appends to the
/// new parent, matching <see cref="UiPatchOperations.InsertNode" />.</item>
/// <item>An <see cref="UiPatchOperations.InsertNode" /> with a <c>null</c> <see cref="Index" /> appends;
/// an index beyond the child count rejects - never clamped, since clamping would hide the bug. The same
/// out-of-bounds rule applies to <see cref="UiPatchOperations.MoveNode" />.</item>
/// <item>An <see cref="UiPatchOperations.InsertNode" />'s <see cref="NodeId" /> must equal
/// <see cref="Node" />'s id; a mismatch rejects.</item>
/// <item><see cref="UiPatchOperations.RemoveNode" /> and <see cref="UiPatchOperations.MoveNode" />
/// targeting the tree's root reject; <see cref="UiPatchOperations.ReplaceNode" /> on the root may not
/// change the root's id.</item>
/// <item>Operations apply in array order, each observing the previous one's effect.</item>
/// </list>
///
/// <para>
/// Rules 2, 3, 5 and 6 need a tree to evaluate and stay a documented renderer contract with no API in
/// this package. Rule 1 and the <c>NodeId</c>/<c>Node.Id</c> half of rule 4 are context-free and are
/// checked by <see cref="UiPatchOperationValidation.Validate" />.
/// </para>
/// </summary>
public sealed record UiPatchOperation
{
	/// <summary>One of <see cref="UiPatchOperations" />. An unrecognised value deserializes fine and
	/// makes the operation - and so the whole patch - inapplicable, never an exception.</summary>
	[JsonPropertyOrder(0)]
	public required string Op { get; init; }

	/// <summary>
	/// The node the operation targets. For <see cref="UiPatchOperations.InsertNode" /> this is the id
	/// being inserted, which must equal <see cref="Node" />'s own id.
	/// </summary>
	[JsonPropertyOrder(1)]
	public required string NodeId { get; init; }

	/// <summary>The parent to insert under or move to. Set for <see cref="UiPatchOperations.InsertNode" />
	/// and <see cref="UiPatchOperations.MoveNode" />. Omitted when absent.</summary>
	[JsonPropertyOrder(2)]
	public string? ParentId { get; init; }

	/// <summary>The child-list index to insert or move to. For <see cref="UiPatchOperations.InsertNode" />,
	/// a <c>null</c> value appends. For <see cref="UiPatchOperations.MoveNode" />, the index in the
	/// parent's child list after the node is removed from its old position; a <c>null</c> value appends
	/// to the new parent. Omitted when absent.</summary>
	[JsonPropertyOrder(3)]
	public int? Index { get; init; }

	/// <summary>Properties to set, for <see cref="UiPatchOperations.SetProperties" />. Values follow the
	/// same unvalidated-by-this-model contract as <see cref="UiNode.Properties" />. Omitted when
	/// absent.</summary>
	[JsonPropertyOrder(4)]
	public IReadOnlyDictionary<string, JsonElement>? Properties { get; init; }

	/// <summary>Property keys to remove, for <see cref="UiPatchOperations.SetProperties" />. A key
	/// present in both this and <see cref="Properties" /> rejects the operation. Omitted when
	/// absent. A null list stays null - and so omitted on write - but a null element inside a non-null
	/// list, <c>[null]</c> on the wire, is dropped rather than deserialized as null.</summary>
	[JsonPropertyOrder(5)]
	public IReadOnlyList<string>? RemovedProperties
	{
		get;
		init => field = DropNullElements(value);
	}

	/// <summary>The node to insert or replace with, for <see cref="UiPatchOperations.InsertNode" /> and
	/// <see cref="UiPatchOperations.ReplaceNode" />. Omitted when absent.</summary>
	[JsonPropertyOrder(6)]
	public UiNode? Node { get; init; }

	/// <summary>Builds a defensive copy of <paramref name="removedProperties" /> with any null element
	/// dropped, or <c>null</c> when <paramref name="removedProperties" /> itself is <c>null</c>. A null
	/// element cannot occur through normal construction - <see cref="RemovedProperties" />'s element type
	/// is annotated non-nullable - but System.Text.Json does not enforce nullable annotations, so a wire
	/// array such as <c>[null]</c> deserializes with a null element unless this normalizes it away. An
	/// explicit loop, not LINQ's <c>Where</c>, because a null check against a non-nullable-annotated
	/// element would trip an analyzer at <c>AnalysisLevel=latest-recommended</c>.</summary>
	private static List<string>? DropNullElements(IReadOnlyList<string>? removedProperties)
	{
		if (removedProperties is null)
		{
			return null;
		}

		var result = new List<string>(removedProperties.Count);

		foreach (var key in removedProperties)
		{
			if (key is not null)
			{
				result.Add(key);
			}
		}

		return result;
	}
}
