using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.Identity;

namespace MacroDeck.Ui.Model.Nodes;

/// <summary>
/// One node of a UI tree: an id, an arbitrary component type name, an arbitrary property map, its
/// children, and the fallback to render when the component is unsupported. This is the whole shape the
/// core model defines - the core declares no node types and validates no surface kinds. A configuration
/// primitive (a text field, a wizard step) and a widget primitive (an image, a chart, a progress bar)
/// are both profiles living in <see cref="Type" /> and <see cref="Properties" />, and neither ever
/// appears in this package.
///
/// <para>
/// Deliberately absent: <c>Key</c>, <c>Slot</c>, <c>Layout</c>, <c>Label</c>, <c>Visible</c>,
/// <c>Disabled</c>, <c>Validation</c>, an events list. Each is a profile property; <c>Label</c> on the
/// node would assert every node is a form field, which is exactly the frozen mistake this model exists
/// to avoid.
/// </para>
///
/// <para>
/// <b>Id stability</b> is a producer contract: the producer emits the same <see cref="Id" /> for the
/// same logical node on every rebuild, the model addresses nodes by id and never by position, and an id
/// derived from array position is a defect. <b>Id uniqueness</b> is a producer obligation with no API
/// here: node ids are unique across the whole tree, including inside <see cref="Fallback" /> subtrees,
/// and a patch may target a node inside a fallback subtree.
/// </para>
///
/// <para>
/// <b>Submission</b>: an input-bearing node's <see cref="Id" /> is the field name it submits as, so
/// submission stays keyed by name in the existing dual-path submit payload, whichever path rendered.
/// </para>
/// </summary>
public sealed record UiNode
{
	/// <summary>The node's id. Must satisfy <see cref="UiIdentifier.IsValid" />; validated by the
	/// producer at the boundary, never by this record.</summary>
	[JsonPropertyOrder(0)]
	public required string Id { get; init; }

	/// <summary>The component name for negotiation - one string, one meaning. An arbitrary,
	/// profile-defined vocabulary; this package neither declares nor validates any value.</summary>
	[JsonPropertyOrder(1)]
	public required string Type { get; init; }

	/// <summary>
	/// The component version this node requires, letting it say "I need chart v2" without a new node
	/// type. <c>null</c> means version 1. Negotiated by
	/// <see cref="Negotiation.UiCapabilityNegotiator.NegotiateComponent" />. Omitted when null.
	/// </summary>
	[JsonPropertyOrder(2)]
	public int? RequiredComponentVersion { get; init; }

	/// <summary>
	/// Arbitrary property data for <see cref="Type" /> to interpret. Values are <b>unvalidated by this
	/// model</b>: a consumer must check <see cref="JsonElement.ValueKind" /> before calling a typed
	/// getter, since a property such as <c>{"rotation":"37.5"}</c> can arrive as a JSON string rather
	/// than a number. A JSON <c>null</c> property value means the key is explicitly set to null, not
	/// removed - removal on a patch is expressed by naming the key in
	/// <see cref="Patches.UiPatchOperation.RemovedProperties" /> instead. Every <see cref="JsonElement" />
	/// value must outlive this record: it must not be backed by a <see cref="JsonDocument" /> that has
	/// since been disposed. Always written, including when empty.
	/// </summary>
	/// <remarks>Not a C# <c>required</c> member: a missing <c>properties</c> key on the wire deserializes
	/// to empty rather than failing, which <c>required</c> would prevent.</remarks>
	[JsonPropertyOrder(3)]
	public IReadOnlyDictionary<string, JsonElement> Properties
	{
		get;
		init => field = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
	} = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

	/// <summary>The node's children, in render order. Never reordered by this model. Always written,
	/// including when empty.</summary>
	/// <remarks>Not a C# <c>required</c> member, for the same reason as <see cref="Properties" />. A null
	/// element - <c>[null]</c> on the wire - is dropped rather than deserialized as null: every consumer
	/// walking <see cref="Children" /> expects a non-null <see cref="UiNode" />.</remarks>
	[JsonPropertyOrder(4)]
	public IReadOnlyList<UiNode> Children
	{
		get;
		init => field = DropNullElements(value);
	} = [];

	/// <summary>
	/// What a renderer shows in this node's place when its component is unsupported - the model-level
	/// answer to unsupported components. The renderer negotiates the fallback subtree in turn; with no
	/// fallback the renderer renders nothing for this node and its children and continues rendering the
	/// rest of the tree. Unsupported never fails the tree and never fails the session. Omitted when
	/// null.
	/// </summary>
	[JsonPropertyOrder(5)]
	public UiNode? Fallback { get; init; }

	/// <summary>Builds a defensive copy of <paramref name="children" /> with any null element dropped. A
	/// null element cannot occur through normal construction - <see cref="Children" /> is annotated
	/// non-nullable - but System.Text.Json does not enforce nullable annotations, so a wire array such as
	/// <c>[null]</c> deserializes with a null element unless this normalizes it away. An explicit loop,
	/// not LINQ's <c>Where</c>, because a null check against a non-nullable-annotated element would trip
	/// an analyzer at <c>AnalysisLevel=latest-recommended</c>.</summary>
	private static List<UiNode> DropNullElements(IReadOnlyList<UiNode>? children)
	{
		var result = new List<UiNode>(children?.Count ?? 0);

		if (children is null)
		{
			return result;
		}

		foreach (var child in children)
		{
			if (child is not null)
			{
				result.Add(child);
			}
		}

		return result;
	}
}
