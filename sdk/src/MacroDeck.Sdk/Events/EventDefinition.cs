using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Sdk.Events;

/// <summary>
/// Describes one event a provider can raise. The host ships this metadata to the UI and the trigger
/// editor builds its form from it, so adding an event needs no UI change.
///
/// Parameters are declared with the same <see cref="ActionParameter" /> schema as action parameters
/// and config-flow fields, which is what makes that true: every control the action builder can
/// already render is available to an event.
/// </summary>
public sealed class EventDefinition
{
	/// <summary>Provider-local id, stable across releases - it is persisted in the widget's data.</summary>
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	public LocalizedText Description { get; init; }

	/// <summary>Groups events in the picker, e.g. "Recording", "Playback". Optional.</summary>
	public LocalizedText Category { get; init; }

	/// <summary>
	/// Name of an icon the UI already ships, shown next to the event in the picker. Providers do not
	/// send image data - the integration's own icon is served separately by the host.
	/// </summary>
	public string? IconName { get; init; }

	public EventDeliveryKind DeliveryKind { get; init; } = EventDeliveryKind.Push;

	/// <summary>
	/// Authored by the user on the trigger: which scene to watch, which variable, how often. For a
	/// <see cref="EventDeliveryKind.Push" /> event these narrow which occurrences the trigger reacts
	/// to; for a <see cref="EventDeliveryKind.Scheduled" /> one they define the schedule itself.
	/// </summary>
	public IReadOnlyList<ActionParameter> ConfigurationParameters { get; init; } = [];

	/// <summary>
	/// What an occurrence carries. Read-only description, never rendered as an input: it populates
	/// the picker that inserts <c>{ "$event": "name" }</c> references into the triggered flow, and
	/// labels the values in the editor's live preview.
	/// </summary>
	public IReadOnlyList<ActionParameter> PayloadParameters { get; init; } = [];
}
