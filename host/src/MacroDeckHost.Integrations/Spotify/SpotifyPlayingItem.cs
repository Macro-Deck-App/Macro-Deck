using SpotifyAPI.Web;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed record SpotifyPlayingItem(string Id, string Uri, string Url)
{
	public static SpotifyPlayingItem? From(IPlayableItem? item) => item switch
	{
		FullTrack { IsLocal: false } track => Build(track.Id, track.Uri, track.ExternalUrls, "track"),
		FullEpisode episode => Build(episode.Id, episode.Uri, episode.ExternalUrls, "episode"),
		_ => null
	};

	private static SpotifyPlayingItem? Build(
		string? id,
		string? uri,
		IReadOnlyDictionary<string, string>? externalUrls,
		string kind)
	{
		if (string.IsNullOrEmpty(id) ||
			string.IsNullOrEmpty(uri) ||
			uri.StartsWith("spotify:local:", StringComparison.Ordinal))
		{
			return null;
		}

		var url = externalUrls?.GetValueOrDefault("spotify") ?? $"https://open.spotify.com/{kind}/{id}";
		return new SpotifyPlayingItem(id, uri, url);
	}
}
