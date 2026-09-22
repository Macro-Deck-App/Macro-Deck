using MacroDeck.Localization;

namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// One widget type a provider offers: the entry the widget picker shows, not the widget itself. A widget of
/// this type is drawn by the provider's <see cref="Ui.IUiProvider" /> when Macro Deck opens a <c>widget</c>
/// surface for it, previewed through a <c>preview</c> surface, and configured through a <c>config</c>
/// surface whose entry point is <c>widget-config</c>.
/// </summary>
/// <param name="Id">
/// Provider-local id, stable across restarts and unique within the provider. Macro Deck qualifies it with
/// the owning provider - <c>your.plugin.id::gauge</c> - and that qualified form is what a widget stores as
/// its type. Must be non-empty. <b>It must stay stable across releases:</b> every widget already placed on a
/// deck names it, and changing it strands all of them.
/// </param>
/// <param name="Name">The type's name as the picker shows it, in the reader's own language.</param>
/// <param name="Description">One sentence under the name in the picker. Absent means none.</param>
/// <param name="DefaultData">
/// The stored configuration a newly added widget of this type starts with, as a JSON object. Absent is read
/// as <c>{}</c>. This is what the picker hands the host when a user adds the widget, so a type that needs
/// non-empty data to draw anything at all should supply it here rather than defending against <c>{}</c> in
/// every session.
/// </param>
/// <param name="DataSchema">
/// A JSON Schema for the widget's stored data. Macro Deck validates every save against it, so a
/// configuration tree cannot write a shape the provider will not be able to read back. Optional for a type
/// whose data is fixed, but <b>required when <paramref name="HasConfiguration" /> is true</b> - a
/// configuration surface writing into an unvalidated payload is precisely how a widget's data becomes
/// unreadable with nothing reporting an error.
/// </param>
/// <param name="HasConfiguration">
/// Whether the provider serves a <c>widget-config</c> configuration surface for this type. False means the
/// widget is not user-configurable and Macro Deck offers no editor for it.
/// </param>
/// <param name="Metadata">Provider-defined metadata. Opaque to Macro Deck.</param>
public sealed record WidgetTypeDescriptor(
	string Id,
	LocalizedText Name,
	LocalizedText? Description = null,
	string? DefaultData = null,
	string? DataSchema = null,
	bool HasConfiguration = false,
	IReadOnlyDictionary<string, string>? Metadata = null)
{
	/// <summary>
	/// Whether Macro Deck runs a widget's own action flows when its tile is pressed, as it does for its
	/// built-in widgets. Default false: a press runs nothing on the host, and the tile only reacts to the
	/// events its tree declares.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When true, a short press, long press, double tap, touch start or touch end that the widget's tree does
	/// not claim runs the flow for that trigger from the top-level <c>flows</c> key of the widget's stored
	/// data - the key a <c>UiActionsListEditor</c> bound to <c>flows</c> writes. A press the tree claims still
	/// goes to the tree and runs no flow. <see cref="DataSchema" /> has to allow <c>flows</c>, or no flow can
	/// be saved.
	/// </para>
	/// <para>
	/// A hardware deck never produces a double tap for a widget of a provider's type, so a Double Tap flow
	/// runs only from the desktop app and the web client. A Macro Deck release older than this flag ignores
	/// it, and presses of the widget run nothing there.
	/// </para>
	/// </remarks>
	public bool SupportsFlows { get; init; }

	/// <summary>
	/// The standard appearance properties a widget of this type draws from its stored data, beyond
	/// <see cref="WidgetAppearanceProperty.Border" /> and <see cref="WidgetAppearanceProperty.BorderColor" />,
	/// which every type has because Macro Deck draws the border itself. Absent or empty means border only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Macro Deck's widget appearance actions (Set Background Color, Set Label, Set Label Color, Set Label
	/// Font, Set Accent Color) accept a widget of this type for each listed property and write the value to the
	/// documented data key named in <c>MacroDeck.Ui.Config.UiWidgetAppearanceKeys</c>. The widget's session is
	/// then reopened with the new data, and the provider draws the value itself; <c>UiWidgetAppearance.Read</c>
	/// reads them all.
	/// </para>
	/// <para>
	/// Only <see cref="WidgetAppearanceProperty.BackgroundColor" />, <see cref="WidgetAppearanceProperty.Label" />,
	/// <see cref="WidgetAppearanceProperty.LabelColor" />, <see cref="WidgetAppearanceProperty.Font" /> and
	/// <see cref="WidgetAppearanceProperty.AccentColor" /> are honoured; any other value is ignored. A listed
	/// property is offered for a widget only if <see cref="DataSchema" /> accepts a plain sample value under
	/// its keys, so the schema has to allow the keys without narrow patterns, enums or ranges. A Macro Deck
	/// release older than this property ignores it and offers border only.
	/// </para>
	/// </remarks>
	public IReadOnlyList<WidgetAppearanceProperty>? AppearanceProperties { get; init; }
}
