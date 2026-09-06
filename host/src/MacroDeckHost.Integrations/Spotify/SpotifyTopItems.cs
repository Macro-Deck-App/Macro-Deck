namespace MacroDeckHost.Integrations.Spotify;

internal sealed record SpotifyTopItems(
	string? TopTrack,
	string? TopArtist,
	IReadOnlyList<string> TopTracks,
	IReadOnlyList<string> TopArtists);
