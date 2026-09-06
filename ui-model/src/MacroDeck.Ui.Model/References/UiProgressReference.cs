using System.Text.Json.Serialization;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.References;

/// <summary>
/// A reference to <b>a position that is still moving</b> - where a medium had reached at one instant, and
/// how fast it has been advancing since. Not the position at the moment the tree was built: the reader
/// carries it forward on its own clock, so a playing track's timeline costs no patch, no message and no
/// session churn, and keeps advancing while the connection is down.
///
/// <para>
/// This is the second reader-resolved reference this profile ships, after
/// <see cref="UiTimeReference" />, and it follows that one's rules exactly. It appears as a property
/// value - <c>{"value":{"$progress":{"positionMs":42000,"durationMs":215000,"anchor":"…"}}}</c> - on node
/// types that declare it, never on a property that previously always carried something else: a reader
/// meeting an unknown <i>type</i> degrades through the node's fallback, whereas a reader meeting an unknown
/// <i>property</i> would silently draw the wrong thing. In particular it never appears on
/// <c>macrodeck.dynamic-text</c>, whose <c>value</c> is a time reference and whose reader rejects any other
/// shape outright.
/// </para>
///
/// <para>
/// <b>The reader resolves it as</b>
/// <c>position(t) = clamp(PositionMs + (t - Anchor) * Rate, 0, DurationMs)</c>, where <c>t</c> is now on
/// the reader's host-synchronised clock and the upper clamp is dropped when <see cref="DurationMs" /> is
/// absent. A reader re-evaluates at least once a second - the same cadence
/// <see cref="UiTimeReference" /> asks of a clock, and enough for both a timeline and a seconds-
/// resolution readout. Sub-second interpolation is neither required nor forbidden: unlike a clock hand's
/// sweep it is derivable from the reference, so two readers that each choose their own still agree on what
/// they are drawing.
/// </para>
///
/// <para>
/// A reference says <i>which</i> position, never how to draw it. The consuming node's own properties carry
/// that, which is what lets a bar and a text run share one value shape.
/// </para>
/// </summary>
[JsonConverter(typeof(UiProgressReferenceJsonConverter))]
public sealed record UiProgressReference
{
	/// <summary>The position, in milliseconds, at <see cref="Anchor" />. Negative values are clamped to zero
	/// by the reader rather than rejected.</summary>
	public required long PositionMs { get; init; }

	/// <summary>The instant <see cref="PositionMs" /> was true, on the producing host's clock - the same
	/// clock a reader already synchronises against to resolve a <see cref="UiTimeReference" />.</summary>
	public required DateTimeOffset Anchor { get; init; }

	/// <summary>The whole length of the medium, in milliseconds. Absent means unknown, and a reader draws no
	/// upper bound rather than guessing one - a live stream has a position and no end.</summary>
	public long? DurationMs { get; init; }

	/// <summary>
	/// How fast the position advances, as a multiple of real time. <b>Absent means <c>1</c></b> - the
	/// ordinary case, and the one whose display changes every second, so it costs no key. A halted medium
	/// carries <c>0</c> explicitly, which is what makes "paused" a value rather than an omission.
	/// </summary>
	public double? Rate { get; init; }

	/// <summary>A position advancing at normal speed from <paramref name="positionMs" /> at
	/// <paramref name="anchor" />.</summary>
	public static UiProgressReference Advancing(long positionMs, DateTimeOffset anchor, long? durationMs = null)
		=> new() { PositionMs = positionMs, Anchor = anchor, DurationMs = durationMs };

	/// <summary>A position that is not advancing at all - <see cref="Rate" /> written as <c>0</c>.</summary>
	public static UiProgressReference Halted(long positionMs, DateTimeOffset anchor, long? durationMs = null)
		=> new() { PositionMs = positionMs, Anchor = anchor, DurationMs = durationMs, Rate = 0 };
}
