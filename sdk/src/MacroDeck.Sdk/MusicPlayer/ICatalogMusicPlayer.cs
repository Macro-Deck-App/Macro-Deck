namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Optional capability of a music player that can play a catalog item directly (e.g. a specific
/// track or playlist), as opposed to only controlling whatever is already playing. The host uses it
/// to fulfil the Play Track / Play Playlist action and the runtime pick dialog. It extends
/// <see cref="IMusicPlayerCatalogProvider"/>, so a player that can play a catalog item can always be
/// browsed as well; discovered by the host through interface casting like the other provider
/// capabilities. A player that implements neither is never asked to play an item.
/// </summary>
public interface ICatalogMusicPlayer : IMusicPlayer, IMusicPlayerCatalogProvider
{
	/// <summary>
	/// Plays a specific catalog item.
	/// </summary>
	/// <remarks>
	/// This is a command, not a read: <b>log and return on failure instead of throwing</b>, so an
	/// action flow continues rather than aborting on playback the user cannot retry from where they
	/// are. Let <see cref="OperationCanceledException"/> propagate - a cancelled play is the caller
	/// leaving, not a provider fault.
	/// </remarks>
	Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default);
}
