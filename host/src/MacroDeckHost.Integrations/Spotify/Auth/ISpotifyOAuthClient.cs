namespace MacroDeckHost.Integrations.Spotify.Auth;

internal interface ISpotifyOAuthClient
{
	Task<SpotifyRefreshedToken> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken);
}

internal sealed record SpotifyRefreshedToken(string AccessToken, string? RefreshToken, int ExpiresIn);
