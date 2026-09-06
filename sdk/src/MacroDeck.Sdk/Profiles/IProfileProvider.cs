namespace MacroDeck.Sdk.Profiles;

/// <summary>
/// Implemented by integrations that ship one or more ready-made, read-only profiles with a fixed
/// layout (e.g. a Spotify Car Thing). The host merges these virtual profiles with the user's own
/// JSON profiles, prefixing provider-local ids as <c>integrationId::localId</c>.
/// </summary>
public interface IProfileProvider
{
	/// <summary>Human-readable provider name, e.g. "Spotify".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name - so the one place a name is stated stays
	/// the integration. Stating a name here still wins, which is what an integration exposing one or
	/// more distinctly-branded providers needs.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>The profiles this provider currently exposes.</summary>
	IReadOnlyList<VirtualProfileDescriptor> GetProfiles();

	/// <summary>
	/// Handles an interaction with one of this provider's virtual widgets. Virtual widgets are not
	/// stored in the host's cache, so the host routes triggers here. Optional: the default is a no-op.
	/// </summary>
	Task HandleWidgetInteractionAsync(
		string profileId,
		string folderId,
		string widgetId,
		WidgetInteraction interaction) => Task.CompletedTask;
}
