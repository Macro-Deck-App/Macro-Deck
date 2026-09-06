using MacroDeckHost.Integrations.Twitch.Auth;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

internal sealed class FakeTwitchOAuthClient : ITwitchOAuthClient
{
	private readonly Lock _lock = new();

	public Queue<TwitchTokenPollResult> PollResults { get; } = new();

	public Queue<Func<TwitchTokens>> RefreshResults { get; } = new();

	public TwitchDeviceCode DeviceCode { get; set; } =
		new("device-code",
			"ABCD-1234",
			"https://www.twitch.tv/activate?device-code=ABCD-1234",
			TimeSpan.FromMinutes(30),
			TimeSpan.FromSeconds(1));

	public Exception? DeviceCodeFailure { get; set; }

	public TwitchTokenIdentity Identity { get; set; } =
		new("12345", "streamer", "client-id", TwitchScopes.All, TimeSpan.FromHours(4));

	public Exception? ValidateFailure { get; set; }

	public Func<Task>? BeforeRefresh { get; init; }

	public List<string> Calls { get; } = [];

	public List<string> RequestedScopes { get; } = [];

	public List<string> UsedClientIds { get; } = [];

	public List<string> UsedRefreshTokens { get; } = [];

	public int RefreshCount { get; private set; }

	public Task<TwitchDeviceCode> RequestDeviceCodeAsync(
		string clientId,
		string scopes,
		CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			Calls.Add("device");
			RequestedScopes.Add(scopes);
			UsedClientIds.Add(clientId);
		}

		return DeviceCodeFailure is not null
			? Task.FromException<TwitchDeviceCode>(DeviceCodeFailure)
			: Task.FromResult(DeviceCode);
	}

	public Task<TwitchTokenPollResult> PollTokenAsync(
		string clientId,
		string scopes,
		string deviceCode,
		CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			Calls.Add("poll");

			return Task.FromResult(PollResults.Count > 0
				? PollResults.Dequeue()
				: new TwitchTokenPollResult(TwitchTokenPollStatus.Pending));
		}
	}

	public async Task<TwitchTokens> RefreshAsync(
		string clientId,
		string refreshToken,
		CancellationToken cancellationToken)
	{
		Func<TwitchTokens> next;
		int attempt;
		lock (_lock)
		{
			Calls.Add("refresh");
			RefreshCount++;
			attempt = RefreshCount;
			UsedRefreshTokens.Add(refreshToken);
			next = RefreshResults.Count > 0
				? RefreshResults.Dequeue()
				: () => Tokens($"access-{attempt}", $"refresh-{attempt}", TimeSpan.FromHours(4));
		}

		if (BeforeRefresh is not null)
		{
			await BeforeRefresh();
		}

		return next();
	}

	public Task<TwitchTokenIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			Calls.Add("validate");
		}

		return ValidateFailure is not null
			? Task.FromException<TwitchTokenIdentity>(ValidateFailure)
			: Task.FromResult(Identity);
	}

	public void Dispose()
	{
	}

	public static TwitchTokens Tokens(string access, string refresh, TimeSpan validFor)
		=> new(access, refresh, DateTimeOffset.UtcNow + validFor, TwitchScopes.All);
}
