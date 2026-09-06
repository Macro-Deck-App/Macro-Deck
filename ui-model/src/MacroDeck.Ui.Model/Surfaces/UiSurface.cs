using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The context a <see cref="Nodes.UiTree" /> is rendered in: what chrome surrounds it, what lifecycle
/// to expect, what routing policy applies. <see cref="Kind" /> and <see cref="SessionMode" /> are
/// arbitrary strings, not a closed set - see <see cref="UiSurfaceKinds" /> and
/// <see cref="UiSessionModes" /> for the well-known values and why neither vocabulary is closed.
/// </summary>
public sealed record UiSurface
{
	/// <summary>What kind of surface this is, for example <see cref="UiSurfaceKinds.Config" /> or
	/// <see cref="UiSurfaceKinds.Widget" />. Unrecognised values round-trip verbatim and are never
	/// fatal.</summary>
	[JsonPropertyOrder(0)]
	public required string Kind { get; init; }

	/// <summary>Who else can be attached to the same session, for example
	/// <see cref="UiSessionModes.Shared" /> or <see cref="UiSessionModes.Exclusive" />. Unrecognised
	/// values round-trip verbatim and are never fatal.</summary>
	[JsonPropertyOrder(1)]
	public required string SessionMode { get; init; }

	/// <summary>Surface-specific data, for example a widget's grid size, so a new surface kind never
	/// needs a new record. Always written, including when empty.</summary>
	/// <remarks>Not a C# <c>required</c> member: a missing <c>attributes</c> key on the wire deserializes
	/// to empty rather than failing.</remarks>
	[JsonPropertyOrder(2)]
	public IReadOnlyDictionary<string, JsonElement> Attributes
	{
		get;
		init => field = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
	} = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
