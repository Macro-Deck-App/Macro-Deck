using MacroDeck.Localization;

namespace MacroDeck.Sdk.FolderViews;

/// <summary>
/// One folder view a provider offers: an entry in the picker a user chooses from, not the view itself. The
/// view is served through the provider's <see cref="Ui.IUiProvider" /> when Macro Deck opens a
/// <c>folder</c> surface for a folder that selected this descriptor.
/// </summary>
/// <param name="Id">
/// Provider-local id, stable across restarts and unique within the provider. Macro Deck qualifies it with
/// the owning provider - <c>your.plugin.id::dashboard</c> - and that qualified form is what a folder
/// stores. Must be non-empty. <b>It must stay stable across releases:</b> folders reference it, and
/// changing it strands every folder already using the view.
/// </param>
/// <param name="Name">The view's name as the picker shows it, in the reader's own language.</param>
/// <param name="Description">One sentence under the name in the picker. Absent means none.</param>
/// <param name="Navigation">
/// Whether Macro Deck draws its own back button - see <see cref="FolderViewNavigation" />. Null is read as
/// <see cref="FolderViewNavigation.Default" />.
/// </param>
/// <param name="HasConfiguration">
/// Whether the provider serves a <c>folder-view-config</c> configuration surface for this view. False means
/// choosing the view configures nothing and Macro Deck shows no configuration section for it.
/// </param>
/// <param name="Metadata">Provider-defined metadata. Opaque to Macro Deck.</param>
public sealed record FolderViewDescriptor(
	string Id,
	LocalizedText Name,
	LocalizedText? Description = null,
	string? Navigation = null,
	bool HasConfiguration = false,
	IReadOnlyDictionary<string, string>? Metadata = null)
{
	/// <summary>The navigation mode, with an absent or unrecognised value read as
	/// <see cref="FolderViewNavigation.Default" /> - an unknown mode must never hide the way back.</summary>
	public string ResolvedNavigation
		=> string.Equals(Navigation, FolderViewNavigation.Hidden, StringComparison.Ordinal)
			? FolderViewNavigation.Hidden
			: FolderViewNavigation.Default;
}
