namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The <see cref="UiSurface.Attributes" /> keys a <see cref="UiSurfaceKinds.Widget" /> surface carries to
/// say which widget is being rendered, and which a <see cref="UiSurfaceKinds.Preview" /> surface carries to
/// render an unsaved draft of one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Data" /> is what lets a provider outside the host render a widget at all. A plugin cannot read
/// the host's stored widgets, so the configuration travels on the surface rather than being looked up - the
/// same reason an action's stored parameters travel on a configuration surface. It also means a provider
/// never has to distinguish a saved widget from a draft: both arrive the same way.
/// </para>
/// <para>
/// Deliberately absent: the widget's grid span. Nothing renders differently for it today, because a widget
/// view expresses every length as a fraction of the size it is given rather than in pixels, and a key no
/// reader reads is a key that can never be removed.
/// </para>
/// </remarks>
public static class UiWidgetSurfaceAttributes
{
	/// <summary>The widget instance being rendered. Absent on a preview, which has no stored widget
	/// yet.</summary>
	public const string WidgetId = "widgetId";

	/// <summary>Which kind of widget, so a provider declines a kind it does not serve rather than guessing
	/// from the configuration's shape.</summary>
	public const string WidgetType = "widgetType";

	/// <summary>The widget's configuration, as the JSON object it is stored as.</summary>
	public const string Data = "data";

	/// <summary>The stored widget an unsaved draft belongs to, so widget-scoped variables in that draft
	/// resolve against the same values the saved widget would see - a label reading
	/// <c>{{ vars.brightness }}</c> shows the same text in an editor's preview as it does on the deck.
	/// Names a widget to resolve against, never one to act on: unlike <see cref="WidgetId" /> it grants no
	/// press, no state and no stored configuration, and the surface still renders <see cref="Data" />.
	/// Absent whenever there is nothing to resolve against - a widget picker's sample, or a widget that has
	/// never been saved - so a provider that ignores this key keeps behaving exactly as it did.</summary>
	public const string VariableScopeWidgetId = "variableScopeWidgetId";

	/// <summary>
	/// The corner radius the reader draws this widget's tile with, in the profile's reference coordinate
	/// space - the same space every length in the tree resolves into, so a view can reason about it
	/// directly. Absent on a surface whose reader has not said, and on every preview, which has no tile;
	/// a view answers that with the profile's own default rather than leaving its content in the corner.
	///
	/// <para>
	/// Admitted where the grid span is not, and for the reason the span is refused: a widget spanning
	/// four cells is drawn with the same corner as one spanning one, so this does not move when the
	/// widget is resized. It is a fact about the box the reader draws, which a rounded corner eats into
	/// from the outside and which no length in the tree can otherwise express - content laid out to the
	/// edge runs under the curve. A radius that changes rebuilds the tree; see ADR 0064.
	/// </para>
	/// </summary>
	public const string CornerRadius = "cornerRadius";

	/// <summary>Present and <c>true</c> when the surface renders the drag ghost of a widget that is also
	/// being rendered live, rather than the tile itself. Two surfaces for one widget are otherwise
	/// identical, so without this a reader reusing a session per widget would hand the ghost and the tile
	/// the same one. Absent means the ordinary reading, so a provider that ignores this key keeps behaving
	/// exactly as it did.</summary>
	public const string Ghost = "ghost";

	/// <summary>Present and <c>true</c> when the surface asks for a representative sample of the widget
	/// rather than its live state - what the widget picker draws in each card. A provider that honours it
	/// must draw without reading anything live: no station, player, variable or bound action is
	/// configured yet at the moment somebody is choosing a widget type. Absent means the ordinary
	/// reading, so a provider that ignores this key keeps behaving exactly as it did.</summary>
	public const string Sample = "sample";
}
