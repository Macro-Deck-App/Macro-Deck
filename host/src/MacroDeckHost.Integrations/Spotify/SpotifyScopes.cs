using SpotifyAPI.Web;

namespace MacroDeckHost.Integrations.Spotify;

internal static class SpotifyScopes
{
	public static readonly IReadOnlyList<string> Required =
	[
		Scopes.UserReadPlaybackState,
		Scopes.UserModifyPlaybackState,
		Scopes.UserReadCurrentlyPlaying,
		Scopes.UserReadPlaybackPosition,
		Scopes.PlaylistReadPrivate,
		Scopes.UserLibraryRead,
		Scopes.UserLibraryModify,
		Scopes.PlaylistModifyPrivate,
		Scopes.PlaylistModifyPublic,
		Scopes.UserTopRead
	];

	public static IReadOnlyList<string> Missing(string? granted)
	{
		var grantedScopes = (granted ?? string.Empty)
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.ToHashSet(StringComparer.Ordinal);

		return Required.Where(scope => !grantedScopes.Contains(scope)).ToList();
	}
}
