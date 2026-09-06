namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Optional capability of a music player integration that can browse its library. The host uses it
/// to populate the Play Track / Play Playlist action parameter (config-time, via dynamic options)
/// and the runtime pick dialog. Implement alongside <see cref="IMusicPlayerProvider"/>; discovered
/// by the host through interface casting like the other provider capabilities.
/// </summary>
public interface IMusicPlayerCatalogProvider
{
	/// <summary>
	/// Returns the tracks or playlists this provider can play for the given instance, optionally
	/// filtered by a free-text search. When <paramref name="filter"/> is null/empty the provider
	/// returns its default list (e.g. liked tracks or the user's playlists). Providers may return a
	/// bounded page.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The host only ever calls this from a REST request, never from a SignalR hub invocation, so it
	/// may take as long as the library needs without stalling anything else. In exchange it must
	/// honour <paramref name="cancellationToken"/>: that token is the caller's aborted request, and
	/// it is the only thing that ends a read the remote service never answers.
	/// </para>
	/// <para>
	/// <b>Throw when the read fails; return an empty list only when the library is genuinely empty.</b>
	/// The host cannot tell those apart on its own, and it has to: an empty list renders as "no tracks",
	/// while a failure renders as "could not load, retry". Swallowing the error and returning empty
	/// reports a library we never saw. Let <see cref="OperationCanceledException"/> propagate too - a
	/// cancelled read is the caller leaving, not a provider fault, and the host logs the two
	/// differently.
	/// </para>
	/// </remarks>
	Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken);
}
