using MacroDeckHost.Integrations.Spotify;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

internal sealed class FakeSpotifyOAuthClient : ISpotifyOAuthClient
{
	private readonly Lock _sync = new();
	private int _refreshes;

	public Queue<Func<SpotifyRefreshedToken>> Results { get; } = new();

	public Func<Task>? BeforeRefresh { get; init; }

	public bool Stall { get; init; }

	public int RefreshCount => Volatile.Read(ref _refreshes);

	public List<string> UsedRefreshTokens { get; } = [];

	public async Task<SpotifyRefreshedToken> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
	{
		var attempt = Interlocked.Increment(ref _refreshes);
		lock (_sync)
		{
			UsedRefreshTokens.Add(refreshToken);
		}

		if (BeforeRefresh is not null)
		{
			await BeforeRefresh();
		}

		if (Stall)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}

		Func<SpotifyRefreshedToken>? scripted;
		lock (_sync)
		{
			scripted = Results.Count > 0 ? Results.Dequeue() : null;
		}

		return scripted is not null
			? scripted()
			: new SpotifyRefreshedToken($"access-{attempt}", $"refresh-{attempt}", 3600);
	}

	public static Func<SpotifyRefreshedToken> Rejected(string message = "invalid_grant")
		=> () => throw new SpotifyAuthRejectedException(message);

	public static Func<SpotifyRefreshedToken> Unreachable(string message = "token endpoint unreachable")
		=> () => throw new SpotifyAuthTransientException(message);

	public static Func<SpotifyRefreshedToken> RateLimited(TimeSpan retryAfter)
		=> () => throw new SpotifyAuthTransientException("Spotify is rate-limiting the token endpoint.")
		{
			IsRateLimit = true, RetryAfter = retryAfter
		};
}
