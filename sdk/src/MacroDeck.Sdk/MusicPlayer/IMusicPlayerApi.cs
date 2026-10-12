namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Reads the music players every provider offers, including other plugins' and Macro Deck's own. Reading needs
/// no permission: any plugin may read any provider's players, as it may already register any player's artwork
/// through <see cref="Ui.IUiResourceRegistry.RegisterMusicPlayerArtworkAsync(string, string, string, CancellationToken)" />.
/// <para>
/// Handlers of <see cref="InstancesChanged" /> subscribed through <see cref="IIntegrationContext.MusicPlayers" />
/// are released when Macro Deck shuts the integration down or initializes it again. Handlers run on a background
/// thread; one that throws is logged and does not affect the others.
/// </para>
/// </summary>
public interface IMusicPlayerApi
{
	/// <summary>The players of every enabled provider. Empty until Macro Deck has sent the first list.</summary>
	IReadOnlyList<MusicPlayerInstanceSnapshot> Instances { get; }

	/// <summary>Raised after <see cref="Instances" /> changed.</summary>
	event EventHandler? InstancesChanged;

	/// <summary>
	/// The average colour of the cover the player shows, as lowercase <c>#rrggbb</c>. While the player shows no
	/// cover, is not connected, or its cover cannot be read, it is the last colour Macro Deck derived for the
	/// player. <c>null</c> when there is none, when Macro Deck knows no player with
	/// <paramref name="instanceId" />, and when Macro Deck could not answer.
	/// </summary>
	/// <param name="instanceId">A <see cref="MusicPlayerInstanceSnapshot.Id" />.</param>
	/// <exception cref="ArgumentException"><paramref name="instanceId" /> is null or empty.</exception>
	Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default);
}
