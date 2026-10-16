using MacroDeckHost.Integrations.Spotify.Protocol;
using MacroDeckHost.Integrations.Spotify.Providers;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Integrations.Spotify.Auth;

internal sealed class SpotifyOAuthClient : ISpotifyOAuthClient
{
	private static readonly SpotifyRequestLimiter _limiter = new(0.2, 3);

	private static readonly OAuthClient _shared = new(SpotifyClientConfig.CreateDefault()
		.WithHTTPClient(new SpotifyThrottledHttpClient(
			SpotifyHttpClients.CreateBounded(SpotifyHttpClients.TokenEndpointTimeout),
			_limiter))
		.WithRetryHandler(new SpotifyRetryHandler()));

	private readonly OAuthClient _oauth;

	public SpotifyOAuthClient()
	{
		_oauth = _shared;
	}

	internal SpotifyOAuthClient(IHTTPClient transport)
	{
		_oauth = new OAuthClient(SpotifyClientConfig.CreateDefault()
			.WithHTTPClient(transport)
			.WithRetryHandler(new SpotifyRetryHandler()));
	}

	public async Task<SpotifyRefreshedToken> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
	{
		try
		{
			using var scope = SpotifyRequestScope.Interactive("token-refresh", "oauth");
			var response = await _oauth.RequestToken(
				new AuthorizationCodeRefreshRequest(clientId, clientSecret, refreshToken),
				cancellationToken);
			return new SpotifyRefreshedToken(response.AccessToken, response.RefreshToken, response.ExpiresIn);
		}
		catch (Exception ex) when (Translate(ex,
			"Spotify rejected the refresh token.",
			"Spotify rejected the stored credentials.",
			cancellationToken) is { } translated)
		{
			throw translated;
		}
	}

	public async Task<SpotifyAuthorizedToken> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		string redirectUri,
		CancellationToken cancellationToken)
	{
		try
		{
			using var scope = SpotifyRequestScope.Interactive("authorization-code-exchange", "setup");
			var response = await _oauth.RequestToken(
				new AuthorizationCodeTokenRequest(clientId, clientSecret, code, new Uri(redirectUri)),
				cancellationToken);
			return new SpotifyAuthorizedToken(response.AccessToken,
				response.RefreshToken,
				response.ExpiresIn,
				response.CreatedAt,
				response.Scope);
		}
		catch (Exception ex) when (Translate(ex,
			"Spotify rejected the authorization code.",
			"Spotify rejected the app credentials or the authorization code.",
			cancellationToken) is { } translated)
		{
			throw translated;
		}
	}

	private static Exception? Translate(
		Exception ex,
		string invalidGrantMessage,
		string rejectedMessage,
		CancellationToken cancellationToken)
	{
		return ex switch
		{
			OperationCanceledException when cancellationToken.IsCancellationRequested => null,
			OperationCanceledException
				=> new SpotifyAuthTransientException("The Spotify token request timed out.", ex),
			APITooManyRequestsException limited
				=> new SpotifyAuthTransientException("Spotify is rate-limiting the token endpoint.", ex)
				{
					IsRateLimit = true, RetryAfter = limited.RetryAfter > TimeSpan.Zero ? limited.RetryAfter : null
				},
			SpotifyThrottledException
				=> new SpotifyAuthTransientException("Macro Deck is pacing its Spotify token requests.", ex),
			APIException when SpotifyMusicPlayer.IsInvalidGrant(ex)
				=> new SpotifyAuthRejectedException(invalidGrantMessage, ex),
			APIException { Response: { } failed }
				when MusicPlayerTransientFailure.IsTransientStatusCode((int)failed.StatusCode)
				=> new SpotifyAuthTransientException($"Spotify's token endpoint answered {(int)failed.StatusCode}.",
					ex),
			APIException => new SpotifyAuthRejectedException(rejectedMessage, ex),
			_ when MusicPlayerTransientFailure.IsNetworkLevel(ex)
				=> new SpotifyAuthTransientException("Spotify's token endpoint could not be reached.", ex),
			_ => null
		};
	}
}
