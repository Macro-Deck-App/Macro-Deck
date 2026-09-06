using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Model.Nodes;

/// <summary>
/// A complete UI tree at a point in time: the revision it was built at, the surface it is rendered in,
/// and its root node. Carries no session id - session ownership is the plugin protocol's layer, not
/// this model's.
/// </summary>
public sealed record UiTree
{
	/// <summary>The revision this tree was built at. A subsequent <see cref="Patches.UiPatch" /> names
	/// the revision it applies from and to; see <see cref="Patches.UiPatchSequencing" />.</summary>
	[JsonPropertyOrder(0)]
	public required int Revision { get; init; }

	/// <summary>The surface this tree is rendered in: what chrome surrounds it, what lifecycle to
	/// expect, what routing policy applies. <c>required</c> because the session opener, not the
	/// producer, decides which surface is being built for.</summary>
	[JsonPropertyOrder(1)]
	public required UiSurface Surface { get; init; }

	/// <summary>The tree's root node.</summary>
	[JsonPropertyOrder(2)]
	public required UiNode Root { get; init; }
}
