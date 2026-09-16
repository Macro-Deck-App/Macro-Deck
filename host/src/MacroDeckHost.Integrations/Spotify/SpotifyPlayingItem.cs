using SpotifyAPI.Web;

namespace MacroDeckHost.Integrations.Spotify;

internal enum SpotifyPlayingItemKind
{
	Track,
	Episode
}

internal sealed record SpotifyPlayingItem(string Id, string Uri, string Url, SpotifyPlayingItemKind Kind)
{
	public static SpotifyPlayingItem? From(IPlayableItem? item) => item switch
	{
		FullTrack { IsLocal: false } track => Build(track.Id,
			track.Uri,
			track.ExternalUrls,
			SpotifyPlayingItemKind.Track),
		FullEpisode episode => Build(episode.Id,
			episode.Uri,
			episode.ExternalUrls,
			SpotifyPlayingItemKind.Episode),
		_ => null
	};

	private static SpotifyPlayingItem? Build(
		string? id,
		string? uri,
		IReadOnlyDictionary<string, string>? externalUrls,
		SpotifyPlayingItemKind kind)
	{
		if (string.IsNullOrEmpty(id) ||
			string.IsNullOrEmpty(uri) ||
			uri.StartsWith("spotify:local:", StringComparison.Ordinal))
		{
			return null;
		}

		var segment = kind == SpotifyPlayingItemKind.Track ? "track" : "episode";
		var url = externalUrls?.GetValueOrDefault("spotify") ?? $"https://open.spotify.com/{segment}/{id}";
		return new SpotifyPlayingItem(id, uri, url, kind);
	}
}
