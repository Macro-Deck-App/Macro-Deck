using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>Shows a player's artwork in the integration's own UI trees.</summary>
public static class MusicPlayerArtworkExtensions
{
	/// <summary>
	/// Resolves <paramref name="artworkId" /> through <see cref="IMusicPlayer.GetArtworkAsync" /> and registers
	/// the artwork under <paramref name="resourceName" />, replacing what the name held, and returns the handle
	/// to put into a tree. Returns <c>null</c> when <paramref name="artworkId" /> is null or empty or the player
	/// has no artwork for it; nothing is registered or removed then, so the name keeps its previous picture.
	/// </summary>
	/// <remarks>
	/// Use one fixed name per place the cover is shown, such as <c>now-playing-cover</c>, rather than one per
	/// track: resources count against the plugin's quota for the whole session. Await one call before starting
	/// the next for the same name, since overlapping calls can finish out of order. Exceptions from
	/// <see cref="IMusicPlayer.GetArtworkAsync" />, including cancellation, reach the caller unchanged.
	/// </remarks>
	/// <exception cref="ArgumentException">The name is not valid, or the artwork is empty, larger than one
	/// resource may be, or of a media type Macro Deck does not accept, such as SVG. Nothing was sent.</exception>
	/// <exception cref="UiResourceException">Macro Deck refused or could not complete the registration.</exception>
	public static async Task<UiResource?> GetArtworkAsUiResourceAsync(this IMusicPlayer player,
		IUiResourceRegistry resources,
		string resourceName,
		string? artworkId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(resourceName);

		if (string.IsNullOrEmpty(artworkId))
		{
			return null;
		}

		var artwork = await player.GetArtworkAsync(artworkId, cancellationToken).ConfigureAwait(false);
		if (artwork is null)
		{
			return null;
		}

		return await resources.RegisterAsync(resourceName, artwork.Data, artwork.MimeType, cancellationToken)
			.ConfigureAwait(false);
	}
}
