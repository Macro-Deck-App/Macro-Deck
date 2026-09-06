namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>Whether a catalog item is an individual track or a whole playlist.</summary>
public enum MusicPlayerCatalogItemKind
{
	Track = 0,
	Playlist = 1
}

/// <summary>
/// A selectable track or playlist a music player can play. Listed by
/// <see cref="IMusicPlayerCatalogProvider"/> for the action-builder track/playlist picker and the
/// runtime pick dialog. <see cref="Id"/> is opaque to the host and resolved by the provider in
/// <see cref="ICatalogMusicPlayer.PlayItemAsync"/>.
/// </summary>
public sealed record MusicPlayerCatalogItem(
	string Id,
	string Title,
	MusicPlayerCatalogItemKind Kind,
	string? Subtitle = null,
	string? ArtworkId = null,
	TimeSpan? Duration = null);
