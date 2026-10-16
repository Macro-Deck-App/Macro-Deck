using MacroDeckHost.Integrations.Spotify.Auth;

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

	public Queue<Func<CancellationToken, Task<SpotifyAuthorizedToken>>> Exchanges { get; } = new();

	public List<string> ExchangedCodes { get; } = [];

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

	public Task<SpotifyAuthorizedToken> ExchangeCodeAsync(
		string clientId,
		string clientSecret,
		string code,
		string redirectUri,
		CancellationToken cancellationToken)
	{
		Func<CancellationToken, Task<SpotifyAuthorizedToken>>? scripted;
		lock (_sync)
		{
			ExchangedCodes.Add(code);
			scripted = Exchanges.Count > 0 ? Exchanges.Dequeue() : null;
		}

		return scripted is not null
			? scripted(cancellationToken)
			: Task.FromResult(new SpotifyAuthorizedToken($"access-{code}",
				$"refresh-{code}",
				3600,
				DateTime.UtcNow,
				null));
	}

	public static Func<SpotifyRefreshedToken> Rejected(string message = "invalid_grant")
	{
		return () => throw new SpotifyAuthRejectedException(message);
	}

	public static Func<SpotifyRefreshedToken> Unreachable(string message = "token endpoint unreachable")
	{
		return () => throw new SpotifyAuthTransientException(message);
	}

	public static Func<SpotifyRefreshedToken> RateLimited(TimeSpan retryAfter)
	{
		return () => throw new SpotifyAuthTransientException("Spotify is rate-limiting the token endpoint.")
		{
			IsRateLimit = true, RetryAfter = retryAfter
		};
	}
}
