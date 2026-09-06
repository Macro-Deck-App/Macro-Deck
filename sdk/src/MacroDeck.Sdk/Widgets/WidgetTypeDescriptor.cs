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
	IReadOnlyDictionary<string, string>? Metadata = null);
