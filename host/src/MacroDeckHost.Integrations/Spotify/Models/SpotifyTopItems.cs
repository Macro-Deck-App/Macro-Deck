namespace MacroDeckHost.Integrations.Spotify.Models;

internal sealed record SpotifyTopItems(
	string? TopTrack,
	string? TopArtist,
	IReadOnlyList<string> TopTracks,
	IReadOnlyList<string> TopArtists);
