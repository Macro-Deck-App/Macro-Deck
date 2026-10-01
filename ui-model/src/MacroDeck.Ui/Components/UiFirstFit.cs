using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Components;

/// <summary>
/// Draws the first of its <see cref="UiContainer.Children" /> whose content fits the box this node is given
/// without truncating or overflowing any text; the last child is the fallback and is drawn when none fits.
/// The reader measures with the font it paints in, so a plugin that cannot know the viewer's font, size or
/// culture still gets the layout that reads best.
///
/// <para>
/// <b>Wire form.</b> A <see cref="UiComponents.FirstFit" /> node whose children are the layouts in order of
/// preference, with no properties of its own. The chosen child is drawn across the whole box, and the choice is
/// made again whenever the box or a text changes, with no round-trip to the view.
/// </para>
///
/// <para>
/// <b>Every layout is built, kept current and painted.</b> All of them are materialized and patched whichever
/// one is on screen, so each counts toward the tree's node and byte limits, and a clock, an image or an input
/// inside one is duplicated per layout. Keep the interactive content identical across layouts. A reader that
/// does not know the type draws <see cref="UiElement.Fallback" />; when none is set, the last child is emitted a
/// second time as the fallback, under ids beneath <c>&lt;id&gt;._fallback</c>, which is why the key
/// <c>_fallback</c> is reserved for the children and why an input cannot sit inside the last child without an
/// explicit fallback. A reader that cannot measure draws the last child, so put the layout that is always
/// acceptable there.
/// </para>
///
/// <para>
/// The node's box comes from its own sizing, never from the layout chosen: give it <see cref="MainSize" />,
/// <see cref="Fill" /> or a parent whose cross axis is definite. Where it has to size itself, it takes the size
/// of its last child. <see cref="UiComponentContainer.MainSize" />, <see cref="UiComponentContainer.Fill" />,
/// <see cref="UiComponentContainer.ColumnSpan" /> and <see cref="UiComponentContainer.RowSpan" /> belong on
/// this node; a child that sets them is rejected. Each child is a single element, not a conditional, a repeat
/// or a fragment.
/// </para>
/// </summary>
public sealed record UiFirstFit : UiComponentContainer
{
	/// <inheritdoc />
	public override string Type => UiComponents.FirstFit;
}
