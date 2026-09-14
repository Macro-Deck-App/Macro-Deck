using MacroDeck.Localization;

namespace MacroDeck.Sdk.ScreenSavers;

/// <summary>
/// One screensaver a provider offers: an entry in the picker a user chooses from in a device's settings, not
/// the screensaver itself. The screensaver is served through the provider's <see cref="Ui.IUiProvider" />
/// when Macro Deck opens a <c>screensaver</c> surface for a device that selected this descriptor.
/// </summary>
/// <param name="Id">
/// Provider-local id, stable across restarts and unique within the provider. Macro Deck qualifies it with
/// the owning provider - <c>your.plugin.id::clock</c> - and that qualified form is what a device stores. Must
/// be non-empty. <b>It must stay stable across releases:</b> devices reference it, and changing it strands
/// every device already using the screensaver.
/// </param>
/// <param name="Name">The screensaver's name as the picker shows it, in the reader's own language.</param>
/// <param name="Description">One sentence under the name in the picker. Absent means none.</param>
/// <param name="HasConfiguration">
/// Whether the provider serves a <c>screensaver-config</c> configuration surface for this screensaver. False
/// means choosing it configures nothing and Macro Deck shows no configuration section for it.
/// </param>
/// <param name="Interactive">
/// Whether input reaches the tree. False, the default, means any touch, click or key press dismisses the
/// screensaver and is swallowed. True means a press on a node that claims it is delivered as an event
/// instead; input anywhere else still dismisses.
/// </param>
/// <param name="Metadata">Provider-defined metadata. Opaque to Macro Deck.</param>
public sealed record ScreenSaverDescriptor(
	string Id,
	LocalizedText Name,
	LocalizedText? Description = null,
	bool HasConfiguration = false,
	bool Interactive = false,
	IReadOnlyDictionary<string, string>? Metadata = null);
