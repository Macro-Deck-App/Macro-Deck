using SpotifyAPI.Web;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyOAuthClient : ISpotifyOAuthClient
{
	private static readonly SpotifyRequestLimiter _limiter = new(ceilingPerSecond: 0.2, burst: 3);

	private static readonly OAuthClient _oauth = new(SpotifyClientConfig.CreateDefault()
		.WithHTTPClient(new SpotifyThrottledHttpClient(
			SpotifyHttpClients.CreateBounded(SpotifyHttpClients.TokenEndpointTimeout),
			_limiter))
		.WithRetryHandler(new SpotifyRetryHandler()));

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
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new SpotifyAuthTransientException("The Spotify token request timed out.", ex);
		}
		catch (APITooManyRequestsException ex)
		{
			throw new SpotifyAuthTransientException("Spotify is rate-limiting the token endpoint.", ex)
			{
				IsRateLimit = true, RetryAfter = ex.RetryAfter > TimeSpan.Zero ? ex.RetryAfter : null
			};
		}
		catch (SpotifyThrottledException ex)
		{
			throw new SpotifyAuthTransientException("Macro Deck is pacing its Spotify token requests.", ex);
		}
		catch (APIException ex) when (SpotifyMusicPlayer.IsInvalidGrant(ex))
		{
			throw new SpotifyAuthRejectedException("Spotify rejected the refresh token.", ex);
		}
		catch (APIException ex) when (ex.Response is { } failed &&
			MusicPlayerTransientFailure.IsTransientStatusCode((int)failed.StatusCode))
		{
			throw new SpotifyAuthTransientException($"Spotify's token endpoint answered {(int)ex.Response.StatusCode}.",
				ex);
		}
		catch (APIException ex)
		{
			throw new SpotifyAuthRejectedException("Spotify rejected the stored credentials.", ex);
		}
		catch (Exception ex) when (MusicPlayerTransientFailure.IsNetworkLevel(ex))
		{
			throw new SpotifyAuthTransientException("Spotify's token endpoint could not be reached.", ex);
		}
	}
}
