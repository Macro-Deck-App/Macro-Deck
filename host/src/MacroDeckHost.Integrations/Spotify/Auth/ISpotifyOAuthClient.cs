namespace MacroDeckHost.Integrations.Spotify.Auth;

internal interface ISpotifyOAuthClient
{
	Task<SpotifyRefreshedToken> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken);

	Task<SpotifyAuthorizedToken> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		string redirectUri,
		CancellationToken cancellationToken);
}

internal sealed record SpotifyRefreshedToken(string AccessToken, string? RefreshToken, int ExpiresIn);

internal sealed record SpotifyAuthorizedToken(
	string AccessToken,
	string RefreshToken,
	int ExpiresIn,
	DateTime CreatedAt,
	string? Scope);
