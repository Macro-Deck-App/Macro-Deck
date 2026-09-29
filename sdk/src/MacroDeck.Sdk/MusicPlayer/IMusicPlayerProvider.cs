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

	/// <summary>
	/// Resolves the player a consumer shows for an instance with its own values for the instance's
	/// <see cref="MusicPlayerInstance.Options"/>, or <c>null</c> if unknown. Defaults to
	/// <see cref="GetPlayer"/>, ignoring the values.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Called only for instances that declare options. The host reads state and artwork from the returned
	/// player on its own schedule, separately from the plain <see cref="GetPlayer"/> player, and never sends
	/// it playback commands: actions keep addressing the plain instance.
	/// </para>
	/// <para>
	/// It may be called for the same values again and again, and there is no signal when a set of values is
	/// no longer shown. Return a cheap or cached object and derive time-based behaviour, such as cycling,
	/// from the clock when the state is read rather than from a timer per set of values.
	/// </para>
	/// <para>
	/// The host caches artwork per instance and <see cref="MusicPlayerState.ArtworkId"/>, shared with the plain
	/// player, so an artwork id must name the same image whichever set of values produced it.
	/// </para>
	/// </remarks>
	IMusicPlayer? GetPlayerWithOptions(MusicPlayerOptionsRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return GetPlayer(request.InstanceId);
	}
}
