namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// The region kinds Macro Deck understands today. Open by construction: the kind is a string, not an
/// enum, so a provider may declare a region this version has never heard of and a later version can name
/// one without breaking either side. A reader that does not recognise a kind skips that region and keeps
/// the rest of the descriptor - it must never discard the whole layout.
/// </summary>
public static class LayoutRegionKinds
{
	/// <summary>A rectangular grid of display keys - the only kind Macro Deck renders a deck onto.</summary>
	public const string Grid = "grid";

	/// <summary>Pressable buttons with no display of their own.</summary>
	public const string Button = "button";

	/// <summary>Rotary encoders or dials, optionally pressable.</summary>
	public const string Encoder = "encoder";

	/// <summary>A touch strip or secondary touch display.</summary>
	public const string TouchStrip = "touch-strip";

	/// <summary>Pedals and other binary inputs.</summary>
	public const string Pedal = "pedal";

	public static readonly IReadOnlyList<string> All = [Grid, Button, Encoder, TouchStrip, Pedal];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? kind) => kind is not null && _known.Contains(kind);
}
