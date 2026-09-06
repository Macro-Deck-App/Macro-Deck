using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.References;

/// <summary>
/// A reference to <b>the current instant, as the reader sees it</b> - not an instant the producer
/// captured. A node carrying one is static: the tree is built once and the displayed value advances on
/// the reader's own cadence, so a ticking widget costs no patch, no message and no session churn, and
/// keeps advancing while the connection is down.
///
/// <para>
/// This is the time analogue of a localization reference, and it is deliberately a
/// <b>component-profile</b> value rather than part of the localization contract every plugin compiles
/// against. It appears as a property value - <c>{"value":{"$time":{"zone":"America/New_York"}}}</c> -
/// on a node type that declares it, never on a property that previously always carried something else:
/// a reader meeting an unknown <i>type</i> degrades through the node's fallback, whereas a reader
/// meeting an unknown <i>property</i> would silently draw the wrong thing.
/// </para>
///
/// <para>
/// A reference says <i>which</i> instant, never how to draw it. The consuming node's own properties
/// carry the format, which is what lets a digital run and a dial share one value shape.
/// </para>
/// </summary>
[JsonConverter(typeof(UiTimeReferenceJsonConverter))]
public sealed record UiTimeReference
{
	/// <summary>
	/// The IANA time zone the instant is read in, for example <c>America/New_York</c>. Absent means the
	/// reader's own zone. A reader that does not recognise the id falls back to its own zone rather than
	/// failing the tree.
	/// </summary>
	public string? Zone { get; init; }

	/// <summary>The current instant in the reader's own zone.</summary>
	public static UiTimeReference Now() => new();

	/// <summary>The current instant in <paramref name="zone" />, or in the reader's own zone when it is
	/// null or empty.</summary>
	public static UiTimeReference InZone(string? zone)
		=> string.IsNullOrWhiteSpace(zone) ? new UiTimeReference() : new UiTimeReference { Zone = zone };
}
