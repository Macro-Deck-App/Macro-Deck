namespace MacroDeck.Ui.Components;

/// <summary>
/// The node type strings the core framework ships: three layout containers, five content leaves and two
/// controls. Nothing here needs to know what Macro Deck is - a renderer draws every one of them from the
/// tree alone.
///
/// <para>
/// <b>Namespaced, unlike the configuration profile.</b> A node's <c>type</c> is one flat open string
/// shared by every profile, and <see cref="Config.UiConfigPrimitives.Stack" /> already spells itself
/// <c>stack</c> with entirely different properties. Prefixing separates the two vocabularies without a
/// second wire field, which is what lets a renderer switch on <c>type</c> alone.
/// </para>
///
/// <para>
/// <b>Why a second namespace exists.</b> <see cref="UiMacroDeckComponents" /> holds the components a
/// reader cannot draw from the tree alone, because it must resolve a Macro Deck-defined reference
/// against its own clock. Everything that needs no such reference belongs here, however
/// Macro Deck-flavoured its styling - which is what keeps the framework usable for UI that has nothing
/// to do with a deck.
/// </para>
///
/// <para>
/// <b>No IsKnown, deliberately</b> - the same reasoning
/// <see cref="Config.UiConfigPrimitives" /> states. A profile vocabulary is open, and the model's answer
/// to an unrecognised type is the node's <c>Fallback</c>, never a failure.
/// </para>
/// </summary>
public static class UiComponents
{
	/// <summary>A one-directional layout container. The root of a view is one of these.</summary>
	public const string Stack = "ui.stack";

	/// <summary>A single run of text, sized as a fraction of the view basis.</summary>
	public const string Text = "ui.text";

	/// <summary>An image resolved from a <see cref="Model.Resources.UiResource" /> handle, fitted into a
	/// square box and never cropped.</summary>
	public const string Image = "ui.image";

	/// <summary>A horizontal track carrying a gradient-filled span, with an optional point marker.</summary>
	public const string RangeBar = "ui.range-bar";

	/// <summary>A draggable level: the interactive counterpart of <see cref="RangeBar" />.</summary>
	public const string Slider = "ui.slider";

	/// <summary>A layout container the user presses: <see cref="Stack" />'s interactive counterpart, which
	/// additionally carries its own artwork and its own ring.</summary>
	public const string Button = "ui.button";

	/// <summary>A container that stacks its children through the depth of the box rather than along an axis
	/// of it, so one element can be drawn behind another.</summary>
	public const string Layer = "ui.layer";

	/// <summary>A series of values drawn as a filled line across the element.</summary>
	public const string Chart = "ui.chart";

	/// <summary>A single line of text the user types: the framework's one text entry.</summary>
	public const string TextField = "ui.text-field";

	/// <summary>A container that scrolls, and asks its producer for more as the user reaches the end of
	/// what it holds.</summary>
	public const string List = "ui.list";

	/// <summary>The types the core framework ships. Not exhaustive of what a renderer may meet - see the
	/// type's remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		Stack, Text, Image, RangeBar, Slider, Button, Layer, Chart, TextField, List,
	];
}
