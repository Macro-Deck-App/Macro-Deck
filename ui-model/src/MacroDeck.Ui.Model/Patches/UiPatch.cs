using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Patches;

/// <summary>
/// A sequence of operations that advances a <see cref="Nodes.UiTree" /> from <see cref="FromRevision" />
/// to <see cref="ToRevision" />. Applies atomically: any operation that cannot apply means the renderer
/// discards the whole patch and requests a full tree - graceful resync, never an exception, never a
/// closed session. See <see cref="UiPatchSequencing" /> for the revision-gating rules.
/// </summary>
public sealed record UiPatch
{
	/// <summary>The revision this patch applies from. A patch applies only when the renderer's current
	/// revision equals this value.</summary>
	[JsonPropertyOrder(0)]
	public required int FromRevision { get; init; }

	/// <summary>The revision this patch advances to. Must be strictly greater than
	/// <see cref="FromRevision" />.</summary>
	[JsonPropertyOrder(1)]
	public required int ToRevision { get; init; }

	/// <summary>The operations to apply, in array order, each observing the previous one's effect. Must
	/// be non-empty. Always written, including when empty - emptiness is what makes the patch
	/// inapplicable, not a structural absence.</summary>
	/// <remarks>Not a C# <c>required</c> member: a missing <c>operations</c> key on the wire
	/// deserializes to empty (and therefore inapplicable, per <see cref="UiPatchSequencing" />) rather
	/// than failing. A null element - <c>[null]</c> on the wire - is dropped rather than deserialized as
	/// null: every consumer walking <see cref="Operations" /> expects a non-null
	/// <see cref="UiPatchOperation" />.</remarks>
	[JsonPropertyOrder(2)]
	public IReadOnlyList<UiPatchOperation> Operations
	{
		get;
		init => field = DropNullElements(value);
	} = [];

	/// <summary>Builds a defensive copy of <paramref name="operations" /> with any null element dropped. A
	/// null element cannot occur through normal construction - <see cref="Operations" /> is annotated
	/// non-nullable - but System.Text.Json does not enforce nullable annotations, so a wire array such as
	/// <c>[null]</c> deserializes with a null element unless this normalizes it away. An explicit loop,
	/// not LINQ's <c>Where</c>, because a null check against a non-nullable-annotated element would trip
	/// an analyzer at <c>AnalysisLevel=latest-recommended</c>.</summary>
	private static List<UiPatchOperation> DropNullElements(IReadOnlyList<UiPatchOperation>? operations)
	{
		var result = new List<UiPatchOperation>(operations?.Count ?? 0);

		if (operations is null)
		{
			return result;
		}

		foreach (var operation in operations)
		{
			if (operation is not null)
			{
				result.Add(operation);
			}
		}

		return result;
	}
}
