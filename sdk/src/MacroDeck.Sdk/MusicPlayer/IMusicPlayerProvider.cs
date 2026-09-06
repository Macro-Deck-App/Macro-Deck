namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Implemented by integrations that expose one or more music players (typically one per
/// configured account). The host enumerates instances across all enabled providers so the user
/// can pick which one a widget shows.
/// </summary>
public interface IMusicPlayerProvider
{
	/// <summary>Human-readable provider name, e.g. "Spotify".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name - so the one place a name is stated stays
	/// the integration. Stating a name here still wins, which is what an integration exposing one or
	/// more distinctly-branded providers needs.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>The currently available player instances (one per configured, connected account).</summary>
	IReadOnlyList<MusicPlayerInstance> GetInstances();

	/// <summary>Resolves a player by its provider-local instance id, or <c>null</c> if unknown.</summary>
	IMusicPlayer? GetPlayer(string instanceId);
}
